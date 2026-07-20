using System.Threading;
using MangosSuperUI.Services.Mpq;

namespace MangosSuperUI.Services;

/// <summary>
/// Singleton service that opens the WoW 1.12.1 client MPQ archives and provides
/// on-demand file extraction for any game asset.
///
/// MPQ search order: reverse alphabetical (patch-5 &gt; patch-4 &gt; patch-2 &gt;
/// patch &gt; model &gt; base) so patch overrides take priority.
///
/// Config: Vmangos:ClientDataPath → "/home/wowvmangos/wowclient/Data"
///
/// === Held vs. live archives (this is the important part) ===
/// The vanilla/base archives (dbc, model, texture, terrain, …, plus Blizzard's
/// own patch.MPQ / patch-2.MPQ) are NEVER written by SuperUI, so they are opened
/// once at startup and held for the app's lifetime — fast, and safe.
///
/// The custom patches SuperUI itself REBUILDS (patch-4 = retextures, patch-5)
/// are a different case: something overwrites the live file in Data while the app
/// runs. If the reader held those open, the deploy copy would collide with the
/// held handle ("...being used by another process") and, worse, could serve a
/// half-written file. So the reader does NOT hold them. On each read it takes a
/// fresh copy of the live file into a private scratch dir and reads THAT. The
/// live Data/patch-4.MPQ is never held open here, so any writer/deploy can
/// overwrite it freely, and the content is always current — a rebuild changes the
/// file, which forces a fresh scratch copy on the next read.
///
/// To avoid re-copying patch-4 on every unrelated asset read (ExtractFile probes
/// every archive for overrides), the scratch copy is keyed on the live file's
/// (last-write-time, size). Unchanged ⇒ reuse the copy; changed (i.e. after a
/// rebuild) ⇒ copy fresh. This is correctness, not a speed cache: it is never
/// stale, it just doesn't copy when nothing changed.
///
/// patch-Z / patch-M / patch-3 are staging/sculpt patches this reader has always
/// skipped entirely (they are read elsewhere, e.g. AdtTerrainReader); that is
/// unchanged.
///
/// === Thread safety ===
/// Held archives use RandomAccess positioned I/O and are set once at startup, so
/// concurrent reads are safe (a ReaderWriterLockSlim guards the list only so the
/// rarely-used Unmount/Remount stay safe; the read side is shared/concurrent).
/// Live patches resolve through a lock-free fast path (stat + reuse the cached
/// copy); only an actual refresh after a rebuild takes a short per-patch lock.
///
/// History: War3Net.IO.Mpq → native StormLib → this managed reader.
/// </summary>
public class MpqReaderService : IDisposable
{
    private readonly ILogger<MpqReaderService> _logger;
    private readonly IConfiguration _config;

    /// <summary>Held (never-rewritten) archives, in load order. Reverse iteration
    /// gives patch-overrides-base semantics. Guarded by _rw.</summary>
    private readonly List<(string Name, MpqArchive Archive)> _archives = new();
    private readonly ReaderWriterLockSlim _rw = new(LockRecursionPolicy.NoRecursion);

    /// <summary>SuperUI-written patches served from fresh scratch copies, highest
    /// priority first (patch-5 before patch-4). Not held open.</summary>
    private readonly List<LivePatch> _livePatches = new();

    private string _scratchDir = "";
    private int _scratchCounter;

    private bool _initialized;
    private readonly object _initLock = new();

    public bool IsInitialized => _initialized;
    public int ArchiveCount => _archives.Count + _livePatches.Count;

    public MpqReaderService(IConfiguration config, ILogger<MpqReaderService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLASSIFICATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Staging/sculpt patches read elsewhere — never touched here.</summary>
    private static bool IsSkipped(string name) =>
        name.StartsWith("patch-Z", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("patch-M", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("patch-3", StringComparison.OrdinalIgnoreCase);

    /// <summary>Custom patches SuperUI rebuilds at runtime ⇒ read fresh, never
    /// hold. Adjust this set if you add more writable patches.</summary>
    private static bool IsLivePatch(string name) =>
        name.StartsWith("patch-4", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("patch-5", StringComparison.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════════════
    // INITIALIZATION (lazy, thread-safe)
    // ═══════════════════════════════════════════════════════════════════

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            Initialize();
        }
    }

    private void Initialize()
    {
        var dataPath = _config["Vmangos:ClientDataPath"]
            ?? _config["SpellCreator:ClientDataPath"]
            ?? "/home/wowvmangos/wowclient/Data";

        if (!Directory.Exists(dataPath))
        {
            _logger.LogWarning("MpqReader: Client data path not found: {Path}", dataPath);
            _initialized = true;
            return;
        }

        // Private scratch dir for fresh copies of the live patches. Cleaned on
        // start so a crash can't leave stale copies behind.
        _scratchDir = Path.Combine(Path.GetTempPath(), "superui-mpq-live");
        try
        {
            if (Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, recursive: true);
            Directory.CreateDirectory(_scratchDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MpqReader: could not prepare scratch dir {Dir}: {Err}", _scratchDir, ex.Message);
        }

        _logger.LogInformation("MpqReader: Opening MPQ archives from {Path}", dataPath);

        var mpqFiles = Directory.GetFiles(dataPath, "*.MPQ", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dataPath, "*.mpq", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var mpqPath in mpqFiles)
        {
            var name = Path.GetFileName(mpqPath);

            if (IsSkipped(name))
                continue;

            // Live patch: record it, do NOT open (read fresh on demand).
            if (IsLivePatch(name))
            {
                _livePatches.Add(new LivePatch(name, mpqPath));
                _logger.LogInformation("MpqReader: {Name} registered as LIVE (read from a fresh copy on demand)", name);
                continue;
            }

            long size = -1;
            try { size = new FileInfo(mpqPath).Length; } catch { }

            try
            {
                var archive = MpqArchive.Open(mpqPath);
                if (archive != null)
                {
                    _archives.Add((name, archive));
                    _logger.LogInformation("MpqReader: Opened {Name} ({Size:N0} bytes)", name, size);
                }
                else
                {
                    _logger.LogWarning("MpqReader: {Name} is not a valid MPQ (no header found)", name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MpqReader: Failed to open {Name} ({Size:N0} bytes): {Err}",
                    name, size, ex.Message);
            }
        }

        // Highest priority first (reverse alphabetical: patch-5 before patch-4).
        _livePatches.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation("MpqReader: {Held} held, {Live} live archive(s)", _archives.Count, _livePatches.Count);
        _initialized = true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIVE-PATCH RESOLUTION (fresh copy keyed on the live file's mtime+size)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class LivePatch
    {
        public readonly string Name;
        public readonly string LivePath;     // the file in the client Data dir
        public readonly object RefreshLock = new();
        public volatile Snapshot? Current;    // in use now
        public Snapshot? PendingDispose;      // replaced last refresh; disposed a generation later (lock-held)

        public LivePatch(string name, string livePath) { Name = name; LivePath = livePath; }
    }

    private sealed class Snapshot
    {
        public long Mtime;
        public long Size;
        public MpqArchive Archive = null!;
        public string ScratchPath = "";
    }

    /// <summary>Return an open archive for this live patch backed by a copy that
    /// matches the current live file; refresh the copy if the live file changed.
    /// Null if the live file is missing or unreadable.</summary>
    private MpqArchive? ResolveLivePatch(LivePatch lp)
    {
        long mtime, size;
        try
        {
            var fi = new FileInfo(lp.LivePath);
            if (!fi.Exists) return lp.Current?.Archive;   // vanished mid-run: fall back to last good copy
            mtime = fi.LastWriteTimeUtc.Ticks;
            size = fi.Length;
        }
        catch { return lp.Current?.Archive; }

        // Fast path: cached copy still matches the live file — no lock, no copy.
        var snap = lp.Current;
        if (snap != null && snap.Mtime == mtime && snap.Size == size)
            return snap.Archive;

        lock (lp.RefreshLock)
        {
            // Re-stat under the lock: another thread may have just refreshed, and
            // the live file may have been mid-write when we first stat'd it.
            try
            {
                var fi = new FileInfo(lp.LivePath);
                if (!fi.Exists) return lp.Current?.Archive;
                mtime = fi.LastWriteTimeUtc.Ticks;
                size = fi.Length;
            }
            catch { return lp.Current?.Archive; }

            snap = lp.Current;
            if (snap != null && snap.Mtime == mtime && snap.Size == size)
                return snap.Archive;

            // Copy the live file to a UNIQUE scratch name (never overwrite a
            // scratch file a reader might still be on) and open it.
            string scratch = Path.Combine(_scratchDir,
                $"{lp.Name}.{Interlocked.Increment(ref _scratchCounter)}.tmpmpq");
            MpqArchive? fresh = null;
            try
            {
                File.Copy(lp.LivePath, scratch, overwrite: false);
                fresh = MpqArchive.Open(scratch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MpqReader: live patch {Name} refresh failed: {Err}", lp.Name, ex.Message);
                try { if (File.Exists(scratch)) File.Delete(scratch); } catch { }
            }

            if (fresh == null)
                return snap?.Archive;   // keep the previous good copy on failure

            var newSnap = new Snapshot { Mtime = mtime, Size = size, Archive = fresh, ScratchPath = scratch };

            // Generational cleanup: dispose the copy replaced in the PRIOR refresh
            // (any reader that had it is long done), then queue THIS one. Caps
            // leaked handles at one per live patch and is safe without locking
            // every read.
            var toKill = lp.PendingDispose;
            lp.PendingDispose = snap;
            lp.Current = newSnap;
            if (toKill != null)
            {
                try { toKill.Archive.Dispose(); } catch { }
                try { if (File.Exists(toKill.ScratchPath)) File.Delete(toKill.ScratchPath); } catch { }
            }

            _logger.LogInformation("MpqReader: live patch {Name} refreshed ({Size:N0} bytes)", lp.Name, size);
            return fresh;
        }
    }

    /// <summary>Live patch archives, highest priority first. Skips any that can't
    /// currently be resolved.</summary>
    private IEnumerable<MpqArchive> LiveArchives()
    {
        for (int i = 0; i < _livePatches.Count; i++)
        {
            var a = ResolveLivePatch(_livePatches[i]);
            if (a != null) yield return a;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MOUNT / UNMOUNT (held archives only — kept for API compatibility)
    // ═══════════════════════════════════════════════════════════════════

    public bool UnmountArchive(string archiveFileName)
    {
        EnsureInitialized();
        MpqArchive? old = null;
        _rw.EnterWriteLock();
        try
        {
            for (int i = 0; i < _archives.Count; i++)
            {
                if (string.Equals(_archives[i].Name, archiveFileName, StringComparison.OrdinalIgnoreCase))
                {
                    old = _archives[i].Archive;
                    _archives.RemoveAt(i);
                    break;
                }
            }
            _allPaths = null;
            try { old?.Dispose(); } catch { }
        }
        finally { _rw.ExitWriteLock(); }
        if (old != null) _logger.LogInformation("MpqReader: unmounted {Name}", archiveFileName);
        return old != null;
    }

    /// <summary>Re-open a HELD archive. Live patches (patch-4/5) refresh
    /// themselves on read, so calling this for them is a harmless no-op.</summary>
    public bool RemountArchive(string mpqPath)
    {
        EnsureInitialized();
        var name = Path.GetFileName(mpqPath);
        if (IsSkipped(name) || IsLivePatch(name)) return false;

        MpqArchive? fresh = null;
        try { if (File.Exists(mpqPath)) fresh = MpqArchive.Open(mpqPath); }
        catch (Exception ex) { _logger.LogWarning("MpqReader: RemountArchive {Path}: {Err}", mpqPath, ex.Message); }

        MpqArchive? old = null;
        _rw.EnterWriteLock();
        try
        {
            for (int i = 0; i < _archives.Count; i++)
            {
                if (string.Equals(_archives[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    old = _archives[i].Archive;
                    _archives.RemoveAt(i);
                    break;
                }
            }
            if (fresh != null)
            {
                _archives.Add((name, fresh));
                _archives.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            _allPaths = null;
            try { old?.Dispose(); } catch { }
        }
        finally { _rw.ExitWriteLock(); }
        return fresh != null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // FILE EXTRACTION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract a file by its MPQ-internal path. Live patches (highest priority)
    /// are checked first, then held archives in reverse order. Null if not found.
    /// </summary>
    public byte[]? ExtractFile(string mpqPath)
    {
        EnsureInitialized();

        // Live patches override everything held.
        foreach (var archive in LiveArchives())
        {
            try
            {
                var data = archive.ReadFile(mpqPath);
                if (data != null) return data;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MpqReader: ExtractFile {Path} threw in live patch: {Type}: {Err}",
                    mpqPath, ex.GetType().Name, ex.Message);
            }
        }

        _rw.EnterReadLock();
        try
        {
            for (int i = _archives.Count - 1; i >= 0; i--)
            {
                var (name, archive) = _archives[i];
                try
                {
                    var data = archive.ReadFile(mpqPath);
                    if (data != null) return data;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "MpqReader: ExtractFile {Path} threw in {Archive}: {Type}: {Err}",
                        mpqPath, name, ex.GetType().Name, ex.Message);
                }
            }
            return null;
        }
        finally { _rw.ExitReadLock(); }
    }

    /// <summary>
    /// Try to extract a model file, attempting multiple extension variations.
    /// ItemDisplayInfo.dbc stores model names without consistent extensions.
    /// </summary>
    public byte[]? ExtractModelFile(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath)) return null;

        if (modelPath.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
            return null;

        string pathNoExt;
        if (modelPath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
            modelPath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            pathNoExt = modelPath[..^4];
        else if (modelPath.EndsWith(".m2", StringComparison.OrdinalIgnoreCase))
            pathNoExt = modelPath[..^3];
        else
            pathNoExt = modelPath;

        string[] extensions = { ".m2", ".mdx", ".M2", ".MDX" };
        string[] pathBases = { pathNoExt, pathNoExt.ToLowerInvariant() };

        foreach (var pb in pathBases)
        {
            foreach (var ext in extensions)
            {
                var data = ExtractFile(pb + ext);
                if (data != null) return data;
            }
        }

        return ExtractFile(modelPath);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HASH-TABLE PROBE (diagnostics, listfile-independent)
    // ═══════════════════════════════════════════════════════════════════

    public List<MpqHit> FindByExactPaths(IEnumerable<string> candidatePaths)
    {
        EnsureInitialized();
        var hits = new List<MpqHit>();
        var candidates = candidatePaths as IList<string> ?? candidatePaths.ToList();

        // Live patches.
        foreach (var archive in LiveArchives())
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    if (!archive.HasFile(candidate)) continue;
                    long size = archive.ReadFile(candidate)?.Length ?? 0;
                    hits.Add(new MpqHit { Path = candidate, Archive = archive.ArchivePath, Size = size });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("MpqReader: FindByExactPaths {Path} threw in live patch: {Type}: {Err}",
                        candidate, ex.GetType().Name, ex.Message);
                }
            }
        }

        _rw.EnterReadLock();
        try
        {
            foreach (var candidate in candidates)
            {
                for (int i = _archives.Count - 1; i >= 0; i--)
                {
                    var (archName, archive) = _archives[i];
                    try
                    {
                        if (!archive.HasFile(candidate)) continue;
                        long size = archive.ReadFile(candidate)?.Length ?? 0;
                        hits.Add(new MpqHit { Path = candidate, Archive = archName, Size = size });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            "MpqReader: FindByExactPaths {Path} threw in {Archive}: {Type}: {Err}",
                            candidate, archName, ex.GetType().Name, ex.Message);
                    }
                }
            }
        }
        finally { _rw.ExitReadLock(); }

        return hits;
    }

    public class MpqHit
    {
        public string Path { get; set; } = "";
        public string Archive { get; set; } = "";
        public long Size { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // LISTFILE-BASED SEARCH (diagnostics)
    // ═══════════════════════════════════════════════════════════════════

    private volatile List<string>? _allPaths;
    private readonly object _allPathsLock = new();

    /// <summary>
    /// Extract the (listfile) from each archive and flatten. Cached; live-patch
    /// changes are not reflected until the cache is cleared — diagnostics only.
    /// </summary>
    public List<string> GetAllPaths()
    {
        EnsureInitialized();
        var cached = _allPaths;
        if (cached != null) return cached;
        lock (_allPathsLock)
        {
            cached = _allPaths;
            if (cached != null) return cached;

            var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var archive in LiveArchives())
                AddListfile(archive, combined);

            _rw.EnterReadLock();
            try
            {
                foreach (var (_, archive) in _archives)
                    AddListfile(archive, combined);
            }
            finally { _rw.ExitReadLock(); }

            var result = combined.ToList();
            _allPaths = result;
            _logger.LogInformation("MpqReader: cached {Count} unique paths from listfiles", result.Count);
            return result;
        }
    }

    private void AddListfile(MpqArchive archive, HashSet<string> into)
    {
        try
        {
            var listfile = archive.ReadFile("(listfile)");
            if (listfile == null || listfile.Length == 0) return;
            var contents = System.Text.Encoding.UTF8.GetString(listfile);
            foreach (var line in contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                into.Add(line.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MpqReader: (listfile) read failed in {Path}: {Err}", archive.ArchivePath, ex.Message);
        }
    }

    public List<string> FindByPartialName(string partial)
    {
        if (string.IsNullOrEmpty(partial)) return new List<string>();
        return GetAllPaths()
            .Where(p => p.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        _rw.EnterWriteLock();
        try
        {
            foreach (var (_, archive) in _archives)
            {
                try { archive.Dispose(); } catch { }
            }
            _archives.Clear();
        }
        finally { _rw.ExitWriteLock(); }
        _rw.Dispose();

        foreach (var lp in _livePatches)
        {
            try { lp.Current?.Archive.Dispose(); } catch { }
            try { lp.PendingDispose?.Archive.Dispose(); } catch { }
        }
        _livePatches.Clear();

        try { if (!string.IsNullOrEmpty(_scratchDir) && Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, true); } catch { }
    }
}