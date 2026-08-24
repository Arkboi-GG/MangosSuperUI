using MangosSuperUI.Services.Mpq;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Read-only mount of a LATER client's MPQ archives — a TBC (2.4.3) or WotLK (3.3.5a) Data folder
/// configured on the Settings page (<see cref="TbcMpqSource.ConfigKey"/> /
/// <see cref="WotlkMpqSource.ConfigKey"/>) — used by the Forges' import sections to browse and
/// extract donor weapons/armor from the later client. It deliberately reuses the managed vanilla
/// <see cref="MpqArchive"/> reader: both 2.4.3 and 3.3.5a ship format-1 archives (44-byte header)
/// whose hi-word/extended fields are only needed for archives over 4 GB, which neither data set
/// reaches (measured: the largest 3.3.5a archive, patch.MPQ, is 4,004,713,057 bytes), and every
/// codec they use for M2/BLP/DBC/skin members is zlib/PKWARE (600/600 sampled 3.3.5a members read
/// clean). Any file using a codec the reader lacks fails per-file with a clear exception instead of
/// poisoning the mount.
///
/// Unlike the main <see cref="MpqReaderService"/>, nothing here is ever rewritten by the app, so
/// all archives are held read-only. The mount re-checks the configured path on access (the
/// settings file reloads live) and remounts when it changes; the weapon index is invalidated with
/// it. All state is guarded by one lock — this is a browse/import surface, not a hot path.
///
/// <see cref="LoadM2"/> is the one place that knows about model versions: TBC files (v260–263) go
/// through the vanilla <see cref="M2Reader"/>; WotLK files (v264) carry their views in an external
/// <c>{Model}00.skin</c> member and go through <see cref="M2WotlkReader"/>. Callers get the same
/// <see cref="M2Model"/> either way.
/// </summary>
public abstract class LegacyMpqSource : IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private string? _mountedPath;                      // null ⇔ nothing mounted
    private readonly List<(string Name, MpqArchive Archive)> _archives = new();
    private List<LegacyWeaponEntry>? _weaponIndex;        // built lazily from the client's ItemDisplayInfo
    private LegacyItemVisualIndex? _visualIndex;       // built lazily from the client's ItemVisual* DBCs
    private string? _mountError;

    protected LegacyMpqSource(IConfiguration config, ILogger logger, string configKey, string key, string label, string logPrefix)
    {
        _config = config;
        _logger = logger;
        ConfigKeyName = configKey;
        Key = key;
        Label = label;
        LogPrefix = logPrefix;
    }

    /// <summary>Configuration key holding the client Data folder (e.g. <c>WeaponForge:TbcDataPath</c>).</summary>
    public string ConfigKeyName { get; }
    /// <summary>Short machine key: "tbc" / "wotlk" — used in endpoint routing, cache folders, audit rows.</summary>
    public string Key { get; }
    /// <summary>Human label: "TBC (2.4.3)" / "WotLK (3.3.5a)".</summary>
    public string Label { get; }
    protected string LogPrefix { get; }

    public string? ConfiguredPath
    {
        get
        {
            var p = _config[ConfigKeyName];
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

    /// <summary>Extract a member by MPQ path across the mounted archives, patch precedence first.
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
                    _logger.LogWarning("{Prefix}: extract {Path} threw in {Archive}: {Err}",
                        LogPrefix, mpqPath, _archives[i].Name, ex.Message);
                }
            }
            return null;
        }
    }

    /// <summary>Cheap member existence check across the mounted archives (hash-table probe).</summary>
    public bool HasFile(string mpqPath)
    {
        var path = ConfiguredPath;
        if (path is null) return false;
        lock (_lock)
        {
            EnsureMountedLocked(path);
            return HasMemberLocked(mpqPath);
        }
    }

    /// <summary>Parse a model member regardless of expansion: v256–263 inline views via
    /// <see cref="M2Reader"/>; v264 via <see cref="M2WotlkReader"/> with its <c>{Model}00.skin</c>.
    /// Null when the member is missing or malformed.</summary>
    public M2Model? LoadM2(string m2MpqPath) => LoadM2Detailed(m2MpqPath).Model;

    /// <summary>Same as <see cref="LoadM2"/> but also returns the raw M2 bytes and a reason on failure.</summary>
    public (M2Model? Model, byte[]? M2Bytes, string? Error) LoadM2Detailed(string m2MpqPath)
    {
        var bytes = ExtractFile(m2MpqPath);
        if (bytes is not { Length: > 8 }) return (null, null, $"Could not extract {m2MpqPath} from the {Label} archives.");
        try
        {
            if (M2WotlkReader.IsWotlk(bytes))
            {
                string skinPath = M2WotlkReader.SkinPathFor(m2MpqPath);
                var skin = ExtractFile(skinPath);
                if (skin is not { Length: > 0 })
                    return (null, bytes, $"{m2MpqPath} is a WotLK (v264) model but its skin profile {skinPath} is missing from the {Label} archives.");
                var model = M2WotlkReader.Parse(bytes, skin);
                return model is null
                    ? (null, bytes, $"The WotLK M2 {m2MpqPath} could not be parsed (malformed header/skin).")
                    : (model, bytes, null);
            }
            var legacy = M2Reader.Parse(bytes);
            return legacy is null
                ? (null, bytes, $"The M2 {m2MpqPath} could not be parsed (unsupported version or malformed).")
                : (legacy, bytes, null);
        }
        catch (Exception ex)
        {
            return (null, bytes, $"{m2MpqPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// The browsable weapon catalog: every ItemDisplayInfo row whose ModelName1 resolves to an
    /// existing <c>Item\ObjectComponents\Weapon\*.m2</c> (or <c>Shield\*.m2</c>) member (hash-table
    /// probe — no listfile dependency). Fields 0–5 of ItemDisplayInfo (id, models, textures, first
    /// icon) sit at the same indices in the 2.4.3 (24-field) and 3.3.5a (25-field) layouts as in
    /// vanilla, so the generic DBC reader covers both. Cached until the configured path changes.
    /// </summary>
    public IReadOnlyList<LegacyWeaponEntry> WeaponIndex()
    {
        var path = ConfiguredPath;
        if (path is null) return Array.Empty<LegacyWeaponEntry>();
        lock (_lock)
        {
            EnsureMountedLocked(path);
            if (_weaponIndex is not null) return _weaponIndex;
            _weaponIndex = BuildWeaponIndexLocked();
            return _weaponIndex;
        }
    }

    /// <summary>The client's own permanent-glow table (<c>ItemDisplayInfo.ItemVisual</c> →
    /// <c>ItemVisuals.dbc</c> → effect models). See <see cref="LegacyItemVisualIndex"/> — this is the
    /// glow source the import pipeline reads besides the model's particle emitters.</summary>
    public LegacyItemVisualIndex ItemVisuals()
    {
        var path = ConfiguredPath;
        if (path is null) return LegacyItemVisualIndex.Build(_ => null, _logger, Label);
        lock (_lock)
        {
            EnsureMountedLocked(path);
            return _visualIndex ??= LegacyItemVisualIndex.Build(ExtractLocked, _logger, Label);
        }
    }

    /// <summary>Lock-held member read — <see cref="ExtractFile"/> would deadlock on the same lock.</summary>
    private byte[]? ExtractLocked(string mpqPath)
    {
        for (int i = _archives.Count - 1; i >= 0; i--)
        {
            try { var data = _archives[i].Archive.ReadFile(mpqPath); if (data != null) return data; }
            catch (Exception ex) { _logger.LogWarning("{Prefix}: extract {Path} threw in {Archive}: {Err}", LogPrefix, mpqPath, _archives[i].Name, ex.Message); }
        }
        return null;
    }

    private bool HasMemberLocked(string member)
    {
        for (int i = _archives.Count - 1; i >= 0; i--)
        {
            try { if (_archives[i].Archive.HasFile(member)) return true; }
            catch { /* corrupt entry — treat as absent */ }
        }
        return false;
    }

    private List<LegacyWeaponEntry> BuildWeaponIndexLocked()
    {
        var list = new List<LegacyWeaponEntry>();
        if (_archives.Count == 0) return list;

        byte[]? dbcBytes = null;
        for (int i = _archives.Count - 1; i >= 0 && dbcBytes is null; i--)
        {
            try { dbcBytes = _archives[i].Archive.ReadFile(WeaponNaming.ItemDisplayInfoMember); }
            catch (Exception ex)
            {
                _logger.LogWarning("{Prefix}: ItemDisplayInfo read threw in {Archive}: {Err}",
                    LogPrefix, _archives[i].Name, ex.Message);
            }
        }
        if (dbcBytes is null)
        {
            _logger.LogWarning("{Prefix}: no ItemDisplayInfo.dbc found in the {Label} archives — weapon index is empty", LogPrefix, Label);
            return list;
        }

        DbcWriterService dbc;
        try { dbc = DbcWriterService.ReadDbc(dbcBytes, Key + ":" + WeaponNaming.ItemDisplayInfoMember); }
        catch (Exception ex)
        {
            _logger.LogWarning("{Prefix}: {Label} ItemDisplayInfo parse failed: {Err}", LogPrefix, Label, ex.Message);
            return list;
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
            if (!HasMemberLocked(m2Path))
            {
                dir = WeaponNaming.ShieldDir;
                m2Path = $@"{dir}\{stem}.m2";
                if (!HasMemberLocked(m2Path)) continue;
            }

            string texStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_TextureName1]);
            list.Add(new LegacyWeaponEntry
            {
                DisplayRow = row[WeaponDisplayInfoRow.F_Id],
                ModelStem = stem,
                M2Path = m2Path,
                TextureStem = texStem,
                BlpPath = texStem.Length > 0 ? $@"{dir}\{texStem}.blp" : null,
                IconStem = dbc.ReadString(row[WeaponDisplayInfoRow.F_InventoryIcon]),
            });
        }

        _logger.LogInformation("{Prefix}: weapon index built — {Count} display rows resolve to weapon/shield models", LogPrefix, list.Count);
        return list;
    }

    /// <summary>(Re)mount when the configured path differs from what is mounted. Lock held.</summary>
    private void EnsureMountedLocked(string path)
    {
        if (string.Equals(_mountedPath, path, StringComparison.OrdinalIgnoreCase)) return;

        DisposeArchivesLocked();
        _weaponIndex = null;
        _visualIndex = null;
        _mountError = null;
        _mountedPath = path;

        if (!Directory.Exists(path))
        {
            _mountError = $"Directory not found: {path}";
            _logger.LogWarning("{Prefix}: {Error}", LogPrefix, _mountError);
            return;
        }

        // Tolerate the client ROOT being configured (e.g. /home/user/wrathclient): if the folder holds
        // no MPQs itself but has a Data\ subfolder that does, mount that instead.
        if (!Directory.EnumerateFiles(path, "*.mpq", SearchOption.TopDirectoryOnly).Any() &&
            !Directory.EnumerateFiles(path, "*.MPQ", SearchOption.TopDirectoryOnly).Any())
        {
            string dataSub = Path.Combine(path, "Data");
            if (Directory.Exists(dataSub) &&
                (Directory.EnumerateFiles(dataSub, "*.mpq", SearchOption.TopDirectoryOnly).Any() ||
                 Directory.EnumerateFiles(dataSub, "*.MPQ", SearchOption.TopDirectoryOnly).Any()))
            {
                _logger.LogInformation("{Prefix}: {Path} has no archives; using its Data subfolder", LogPrefix, path);
                path = dataSub;
            }
        }

        // Base archives (common/expansion/lichking/patch/patch-2/patch-3) at the top level, then the
        // locale folder's archives (enUS\locale-enUS.MPQ, patch-enUS.MPQ, patch-enUS-2.MPQ …) — both
        // 2.4.3 and 3.3.5a clients keep DBFilesClient\ItemDisplayInfo.dbc ONLY in the locale archives,
        // so without them the browse is empty. Later in the list = higher precedence (ExtractFile
        // walks it backwards).
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
                .Where(f => !Path.GetFileName(f).Contains("speech-", StringComparison.OrdinalIgnoreCase)) // voice-over only
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
                    _logger.LogInformation("{Prefix}: opened {Name}", LogPrefix, name);
                }
                else
                {
                    _logger.LogWarning("{Prefix}: {Name} has no MPQ header — skipped", LogPrefix, name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{Prefix}: failed to open {Name}: {Err}", LogPrefix, name, ex.Message);
            }
        }

        if (_archives.Count == 0)
            _mountError = $"No readable MPQ archives in {path}";
        _logger.LogInformation("{Prefix}: mounted {Count} archive(s) from {Path}", LogPrefix, _archives.Count, path);
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
/// <summary>One browsable later-client weapon/shield: a display row whose model resolves in the
/// mounted archives. Lane-neutral — both lanes produce these.</summary>
public sealed record LegacyWeaponEntry
{
    public required uint DisplayRow { get; init; }
    /// <summary>Model stem without directory/extension, e.g. "Sword_2H_Blood_D_02".</summary>
    public required string ModelStem { get; init; }
    public required string M2Path { get; init; }
    public required string TextureStem { get; init; }
    public string? BlpPath { get; init; }
    public required string IconStem { get; init; }
}
