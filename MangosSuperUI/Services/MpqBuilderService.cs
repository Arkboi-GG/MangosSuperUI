using System.Runtime.InteropServices;
using System.Text;

namespace MangosSuperUI.Services;

/// <summary>
/// Creates MPQ patch archives for WoW client distribution — via STORMLIB.
///
/// ═══════════════════════════════════════════════════════════════════════
/// WHY STORMLIB AND NOT War3Net.IO.Mpq (July 12 rewrite)
///
/// The previous implementation built archives with War3Net's MpqArchive.Create.
/// Those archives verified fine in War3Net's own reader and were UNREADABLE by
/// everything else. Measured, not suspected:
///
///   • mpyq (independent Python reader) refused the archive outright
///     ("Encryption is not supported"), starting at the (listfile).
///   • The header declared blockEntries=10310 for a build whose log showed
///     12,419 queued files — two thousand files missing from the block table.
///   • The hash table showed 16,384 of 16,384 entries in use — 100% saturated
///     by 12,419 files, which is arithmetically impossible for a valid table
///     and fatal on its own: MPQ lookup probes linearly until it finds an
///     empty slot, so a fully-saturated table cannot terminate a failed lookup.
///   • In the 1.12 client: vanilla rows resolved, appended rows resolved to
///     garbage — gloves rendering a weapon icon with no texture — even after
///     the contained DBC was byte-verified correct on disk.
///
/// The pattern of the whole incident was "our writer, their reader". StormLib
/// is the reference implementation, the same code lineage the client itself
/// embeds, and the same library MpqReaderService already uses on the read
/// side. Using it ends that class of bug rather than patching one instance.
///
/// Build() also SELF-VERIFIES: after writing, it reopens the archive and
/// round-trips every file byte-for-byte against what was queued. A build that
/// does not verify FAILS and deletes its output. Today's failure mode —
/// Build() returns true, archive is garbage — is not allowed to exist anymore.
/// ═══════════════════════════════════════════════════════════════════════
///
/// The public surface (AddFile / AddFileFromDisk / FileCount / TotalSize /
/// GetQueuedPaths / Build / Clear) is unchanged from the War3Net version, so
/// no caller needs to change.
///
/// Requires libstorm.so (StormLib built with -DBUILD_SHARED_LIBS=ON) on the
/// library path.
/// </summary>
public class MpqBuilderService
{
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly ILogger<MpqBuilderService>? _logger;

    public MpqBuilderService(ILogger<MpqBuilderService>? logger = null)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // StormLib interop (subset — create/write/read/close)
    // ─────────────────────────────────────────────────────────────

    private const string STORM = "storm";   // libstorm.so

    private const uint MPQ_CREATE_LISTFILE = 0x00100000;
    private const uint MPQ_CREATE_ARCHIVE_V1 = 0x00000000; // vanilla-era format

    private const uint MPQ_FILE_COMPRESS = 0x00000200;
    private const uint MPQ_FILE_REPLACEEXISTING = 0x80000000;

    private const uint MPQ_COMPRESSION_ZLIB = 0x02;

    private const uint SFILE_INVALID_SIZE = 0xFFFFFFFF;

    [DllImport(STORM)]
    private static extern bool SFileCreateArchive(
        [MarshalAs(UnmanagedType.LPStr)] string mpqName,
        uint createFlags, uint maxFileCount, out IntPtr hMpq);

    [DllImport(STORM)]
    private static extern bool SFileOpenArchive(
        [MarshalAs(UnmanagedType.LPStr)] string mpqName,
        uint priority, uint openFlags, out IntPtr hMpq);

    [DllImport(STORM)]
    private static extern bool SFileCloseArchive(IntPtr hMpq);

    [DllImport(STORM)]
    private static extern bool SFileCreateFile(
        IntPtr hMpq,
        [MarshalAs(UnmanagedType.LPStr)] string archivedName,
        ulong fileTime, uint fileSize, uint locale, uint flags, out IntPtr hFile);

    [DllImport(STORM)]
    private static extern bool SFileWriteFile(
        IntPtr hFile, byte[] data, uint size, uint compression);

    [DllImport(STORM)]
    private static extern bool SFileFinishFile(IntPtr hFile);

    [DllImport(STORM)]
    private static extern bool SFileOpenFileEx(
        IntPtr hMpq,
        [MarshalAs(UnmanagedType.LPStr)] string fileName,
        uint searchScope, out IntPtr hFile);

    [DllImport(STORM)]
    private static extern uint SFileGetFileSize(IntPtr hFile, out uint fileSizeHigh);

    [DllImport(STORM)]
    private static extern bool SFileReadFile(
        IntPtr hFile, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);

    [DllImport(STORM)]
    private static extern bool SFileCloseFile(IntPtr hFile);

    [DllImport(STORM)]
    private static extern bool SFileFlushArchive(IntPtr hMpq);

    // StormLib is not thread-safe per-handle; MpqReaderService serializes on a
    // lock for the same reason. Builds are rare and coarse; one lock suffices.
    private static readonly object _stormLock = new();

    // ─────────────────────────────────────────────────────────────
    // Public surface — unchanged from the War3Net version
    // ─────────────────────────────────────────────────────────────

    /// <summary>Add a file to the MPQ with the given virtual path.</summary>
    public void AddFile(string mpqPath, byte[] data)
    {
        string normalizedPath = mpqPath.Replace('/', '\\');
        _files[normalizedPath] = data;
        _logger?.LogInformation("MpqBuilder: Queued {Path} ({Size} bytes)", normalizedPath, data.Length);
    }

    /// <summary>Add a file from disk.</summary>
    public void AddFileFromDisk(string mpqPath, string diskPath)
    {
        if (!File.Exists(diskPath))
            throw new FileNotFoundException($"File not found: {diskPath}");
        AddFile(mpqPath, File.ReadAllBytes(diskPath));
    }

    public int FileCount => _files.Count;
    public long TotalSize => _files.Values.Sum(f => (long)f.Length);
    public IReadOnlyCollection<string> GetQueuedPaths() => _files.Keys;

    /// <summary>
    /// Build the archive, then REOPEN it and verify every file round-trips
    /// byte-identical. Returns false — and deletes the output — on any failure.
    /// </summary>
    public bool Build(string outputPath)
    {
        if (_files.Count == 0)
        {
            _logger?.LogWarning("MpqBuilder: No files to package");
            return false;
        }

        lock (_stormLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // StormLib amends archives in place; a stale file at the target
                // path would be updated, not replaced. Start clean.
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                // +1 for the (listfile) StormLib maintains itself. StormLib sizes
                // and manages the hash/block tables internally — the entire class
                // of "we computed the table size wrong" bugs (the ushort wrap,
                // the +4 fudge, the 32768 ceiling guard) is gone, not fixed.
                uint maxFiles = (uint)(_files.Count + 1);

                if (!SFileCreateArchive(outputPath,
                        MPQ_CREATE_ARCHIVE_V1 | MPQ_CREATE_LISTFILE,
                        maxFiles, out IntPtr hMpq))
                {
                    _logger?.LogError("MpqBuilder: SFileCreateArchive failed (errno {Err}) for {Path}",
                        Marshal.GetLastWin32Error(), outputPath);
                    return false;
                }

                try
                {
                    foreach (var (mpqPath, data) in _files)
                    {
                        if (!SFileCreateFile(hMpq, mpqPath, 0, (uint)data.Length, 0,
                                MPQ_FILE_COMPRESS | MPQ_FILE_REPLACEEXISTING, out IntPtr hFile))
                        {
                            _logger?.LogError("MpqBuilder: SFileCreateFile failed for {Path} (errno {Err})",
                                mpqPath, Marshal.GetLastWin32Error());
                            return false;
                        }

                        bool ok = data.Length == 0
                            || SFileWriteFile(hFile, data, (uint)data.Length, MPQ_COMPRESSION_ZLIB);
                        ok &= SFileFinishFile(hFile);

                        if (!ok)
                        {
                            _logger?.LogError("MpqBuilder: write failed for {Path} (errno {Err})",
                                mpqPath, Marshal.GetLastWin32Error());
                            return false;
                        }
                    }

                    SFileFlushArchive(hMpq);
                }
                finally
                {
                    SFileCloseArchive(hMpq);
                }

                // ── SELF-VERIFICATION ──
                // "Build() returned true but the client sees garbage" burned an
                // entire day. It is no longer a possible outcome of this method.
                if (!VerifyArchive(outputPath))
                {
                    _logger?.LogError(
                        "MpqBuilder: VERIFICATION FAILED for {Path} — deleting the corrupt output",
                        outputPath);
                    try { File.Delete(outputPath); } catch { /* best effort */ }
                    return false;
                }

                var fileInfo = new FileInfo(outputPath);
                _logger?.LogInformation(
                    "MpqBuilder: Created {Path} ({FileCount} files, {Size} bytes) — verified round-trip OK",
                    outputPath, _files.Count, fileInfo.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MpqBuilder: Failed to create MPQ at {Path}", outputPath);
                return false;
            }
        }
    }

    /// <summary>
    /// Reopen the archive and confirm every queued file extracts byte-identical.
    /// Caller holds _stormLock.
    /// </summary>
    private bool VerifyArchive(string path)
    {
        if (!SFileOpenArchive(path, 0, 0, out IntPtr hMpq))
        {
            _logger?.LogError("MpqBuilder: verify — cannot reopen archive (errno {Err})",
                Marshal.GetLastWin32Error());
            return false;
        }

        try
        {
            int verified = 0;
            foreach (var (mpqPath, expected) in _files)
            {
                if (!SFileOpenFileEx(hMpq, mpqPath, 0, out IntPtr hFile))
                {
                    _logger?.LogError("MpqBuilder: verify — {Path} MISSING from archive (errno {Err})",
                        mpqPath, Marshal.GetLastWin32Error());
                    return false;
                }

                try
                {
                    uint size = SFileGetFileSize(hFile, out _);
                    if (size == SFILE_INVALID_SIZE || size != (uint)expected.Length)
                    {
                        _logger?.LogError(
                            "MpqBuilder: verify — {Path} size mismatch (archive={A}, queued={Q})",
                            mpqPath, size, expected.Length);
                        return false;
                    }

                    // Full content compare for every file. ~12k files / ~35MB is a
                    // one-to-two-second pass. Corruption a size check misses —
                    // right length, wrong bytes — is exactly what the verified-DBC
                    // vs garbage-client incident looked like from the outside.
                    var buf = new byte[size];
                    if (size > 0 &&
                        (!SFileReadFile(hFile, buf, size, out uint read, IntPtr.Zero) || read != size))
                    {
                        _logger?.LogError("MpqBuilder: verify — {Path} read failed (errno {Err})",
                            mpqPath, Marshal.GetLastWin32Error());
                        return false;
                    }
                    if (size > 0 && !buf.AsSpan().SequenceEqual(expected))
                    {
                        _logger?.LogError(
                            "MpqBuilder: verify — {Path} CONTENT differs after round-trip", mpqPath);
                        return false;
                    }
                }
                finally
                {
                    SFileCloseFile(hFile);
                }
                verified++;
            }

            _logger?.LogInformation(
                "MpqBuilder: verify — {N} file(s) round-tripped byte-identical", verified);
            return true;
        }
        finally
        {
            SFileCloseArchive(hMpq);
        }
    }

    /// <summary>Clear all queued files.</summary>
    public void Clear()
    {
        _files.Clear();
    }
}