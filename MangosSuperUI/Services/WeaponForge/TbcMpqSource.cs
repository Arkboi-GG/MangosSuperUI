using MangosSuperUI.Services.Mpq;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Read-only mount of a SECOND client's MPQ archives — a TBC (2.4.3) Data folder configured at
/// <c>WeaponForge:TbcDataPath</c> on the Settings page — used by the Forge's TBC-import section to
/// browse and extract donor weapons from the later client. It deliberately reuses the managed
/// vanilla <see cref="MpqArchive"/> reader: TBC's format-2 headers keep every v1 field at the same
/// offset and only add hi-word/extended fields needed for archives over 4 GB, which the 2.4.3 data
/// set never reaches — so the archives open as-is, and any file using a codec the reader lacks
/// (e.g. bzip2) fails per-file with a clear exception instead of poisoning the mount.
///
/// Unlike the main <see cref="MpqReaderService"/>, nothing here is ever rewritten by the app, so
/// all archives are held read-only. The mount re-checks the configured path on access (the
/// settings file reloads live) and remounts when it changes; the weapon index is invalidated with
/// it. All state is guarded by one lock — this is a browse/import surface, not a hot path.
/// </summary>
public sealed class TbcMpqSource : IDisposable
{
    public const string ConfigKey = "WeaponForge:TbcDataPath";

    private readonly IConfiguration _config;
    private readonly ILogger<TbcMpqSource> _logger;

    private readonly object _lock = new();
    private string? _mountedPath;                      // null ⇔ nothing mounted
    private readonly List<(string Name, MpqArchive Archive)> _archives = new();
    private List<TbcWeaponEntry>? _weaponIndex;        // built lazily from the TBC ItemDisplayInfo
    private string? _mountError;

    public TbcMpqSource(IConfiguration config, ILogger<TbcMpqSource> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string? ConfiguredPath
    {
        get
        {
            var p = _config[ConfigKey];
            return string.IsNullOrWhiteSpace(p) ? null : p.Trim();
        }
    }

    /// <summary>Current mount state for the status endpoint. Mount errors are reported, not thrown.</summary>
    public (bool Configured, string? Path, int ArchiveCount, string? Error) Status()
    {
        var path = ConfiguredPath;
        if (path is null) return (false, null, 0, null);
        lock (_lock)
        {
            EnsureMountedLocked(path);
            return (true, path, _archives.Count, _mountError);
        }
    }

    /// <summary>Extract a member by MPQ path across the TBC archives, patch precedence first.
    /// Null when unconfigured, unmounted, or not found.</summary>
    public byte[]? ExtractFile(string mpqPath)
    {
        var path = ConfiguredPath;
        if (path is null) return null;
        lock (_lock)
        {
            EnsureMountedLocked(path);
            for (int i = _archives.Count - 1; i >= 0; i--)
            {
                try
                {
                    var data = _archives[i].Archive.ReadFile(mpqPath);
                    if (data != null) return data;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("TbcMpq: extract {Path} threw in {Archive}: {Err}",
                        mpqPath, _archives[i].Name, ex.Message);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The browsable TBC weapon catalog: every ItemDisplayInfo row whose ModelName1 resolves to an
    /// existing <c>Item\ObjectComponents\Weapon\*.m2</c> member (hash-table probe — no listfile
    /// dependency). Fields 0–5 of ItemDisplayInfo (id, models, textures, icon) sit at the same
    /// indices in the 2.4.3 layout as in vanilla, so the generic DBC reader covers it. Cached until
    /// the configured path changes.
    /// </summary>
    public IReadOnlyList<TbcWeaponEntry> WeaponIndex()
    {
        var path = ConfiguredPath;
        if (path is null) return Array.Empty<TbcWeaponEntry>();
        lock (_lock)
        {
            EnsureMountedLocked(path);
            if (_weaponIndex is not null) return _weaponIndex;
            _weaponIndex = BuildWeaponIndexLocked();
            return _weaponIndex;
        }
    }

    private List<TbcWeaponEntry> BuildWeaponIndexLocked()
    {
        var list = new List<TbcWeaponEntry>();
        if (_archives.Count == 0) return list;

        byte[]? dbcBytes = null;
        for (int i = _archives.Count - 1; i >= 0 && dbcBytes is null; i--)
        {
            try { dbcBytes = _archives[i].Archive.ReadFile(WeaponNaming.ItemDisplayInfoMember); }
            catch (Exception ex)
            {
                _logger.LogWarning("TbcMpq: ItemDisplayInfo read threw in {Archive}: {Err}",
                    _archives[i].Name, ex.Message);
            }
        }
        if (dbcBytes is null)
        {
            _logger.LogWarning("TbcMpq: no ItemDisplayInfo.dbc found in the TBC archives — weapon index is empty");
            return list;
        }

        DbcWriterService dbc;
        try { dbc = DbcWriterService.ReadDbc(dbcBytes, "tbc:" + WeaponNaming.ItemDisplayInfoMember); }
        catch (Exception ex)
        {
            _logger.LogWarning("TbcMpq: TBC ItemDisplayInfo parse failed: {Err}", ex.Message);
            return list;
        }

        bool HasMember(string member)
        {
            for (int i = _archives.Count - 1; i >= 0; i--)
            {
                try { if (_archives[i].Archive.HasFile(member)) return true; }
                catch { /* corrupt entry — treat as absent */ }
            }
            return false;
        }

        foreach (var row in dbc.GetAllRows())
        {
            if (row.Length < 6) continue;
            string model = dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName1]);
            if (model.Length == 0) continue;
            // Paired models (fist weapons) are excluded; a second model naming the SAME file is the
            // stock thrown-weapon shape and stays importable (the Forge mirrors it on its own row).
            string model2 = dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName2]);
            if (model2.Length != 0 && !string.Equals(model2, model, StringComparison.OrdinalIgnoreCase)) continue;

            string stem = StripToStem(model);
            if (stem.Length == 0) continue;
            // Weapons (melee + ranged) live in the Weapon folder, shields in the Shield folder; the
            // DBC name is bare, so probe both and remember which one the member came from.
            string dir = WeaponNaming.WeaponDir;
            string m2Path = $@"{dir}\{stem}.m2";
            if (!HasMember(m2Path))
            {
                dir = WeaponNaming.ShieldDir;
                m2Path = $@"{dir}\{stem}.m2";
                if (!HasMember(m2Path)) continue;
            }

            string texStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_TextureName1]);
            list.Add(new TbcWeaponEntry
            {
                DisplayRow = row[WeaponDisplayInfoRow.F_Id],
                ModelStem = stem,
                M2Path = m2Path,
                TextureStem = texStem,
                BlpPath = texStem.Length > 0 ? $@"{dir}\{texStem}.blp" : null,
                IconStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_InventoryIcon]),
            });
        }

        _logger.LogInformation("TbcMpq: weapon index built — {Count} display rows resolve to weapon models", list.Count);
        return list;
    }

    /// <summary>(Re)mount when the configured path differs from what is mounted. Lock held.</summary>
    private void EnsureMountedLocked(string path)
    {
        if (string.Equals(_mountedPath, path, StringComparison.OrdinalIgnoreCase)) return;

        DisposeArchivesLocked();
        _weaponIndex = null;
        _mountError = null;
        _mountedPath = path;

        if (!Directory.Exists(path))
        {
            _mountError = $"Directory not found: {path}";
            _logger.LogWarning("TbcMpq: {Error}", _mountError);
            return;
        }

        // Base archives (common/expansion/patch/patch-2) at the top level, then the locale folder's
        // archives (enUS\locale-enUS.MPQ, patch-enUS.MPQ, patch-enUS-2.MPQ …) — a 2.4.3 client keeps
        // DBFilesClient\ItemDisplayInfo.dbc ONLY in the locale archives, so without them the browse
        // is empty. Later in the list = higher precedence (ExtractFile walks it backwards).
        var mpqFiles = Directory.GetFiles(path, "*.MPQ", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(path, "*.mpq", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => Path.GetFileName(f), Comparer<string>.Create(MpqPatchOrder.CompareAscending))
            .ToList();
        foreach (var localeDir in Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string localeName = Path.GetFileName(localeDir);
            if (localeName.Length != 4) continue; // enUS / enGB / deDE … — skip unrelated folders
            var localeFiles = Directory.GetFiles(localeDir, "*.MPQ", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(localeDir, "*.mpq", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f => !Path.GetFileName(f).StartsWith("speech-", StringComparison.OrdinalIgnoreCase)) // voice-over only
                .OrderBy(f => Path.GetFileName(f), Comparer<string>.Create(MpqPatchOrder.CompareAscending))
                .ToList();
            mpqFiles.AddRange(localeFiles);
        }

        foreach (var mpqPath in mpqFiles)
        {
            var name = Path.GetFileName(mpqPath);
            try
            {
                var archive = MpqArchive.Open(mpqPath);
                if (archive != null)
                {
                    _archives.Add((name, archive));
                    _logger.LogInformation("TbcMpq: opened {Name}", name);
                }
                else
                {
                    _logger.LogWarning("TbcMpq: {Name} has no MPQ header — skipped", name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("TbcMpq: failed to open {Name}: {Err}", name, ex.Message);
            }
        }

        if (_archives.Count == 0)
            _mountError = $"No readable MPQ archives in {path}";
        _logger.LogInformation("TbcMpq: mounted {Count} archive(s) from {Path}", _archives.Count, path);
    }

    private void DisposeArchivesLocked()
    {
        foreach (var (_, a) in _archives)
        {
            try { a.Dispose(); } catch { }
        }
        _archives.Clear();
    }

    private static string StripToStem(string modelName)
    {
        int slash = modelName.LastIndexOfAny(['\\', '/']);
        string file = slash >= 0 ? modelName[(slash + 1)..] : modelName;
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    public void Dispose()
    {
        lock (_lock) DisposeArchivesLocked();
    }
}

/// <summary>One browsable TBC weapon: a display row whose model resolves in the TBC archives.</summary>
public sealed record TbcWeaponEntry
{
    public required uint DisplayRow { get; init; }
    /// <summary>Model stem without directory/extension, e.g. "Sword_2H_Blood_D_02".</summary>
    public required string ModelStem { get; init; }
    public required string M2Path { get; init; }
    public required string TextureStem { get; init; }
    public string? BlpPath { get; init; }
    public required string IconStem { get; init; }
}
