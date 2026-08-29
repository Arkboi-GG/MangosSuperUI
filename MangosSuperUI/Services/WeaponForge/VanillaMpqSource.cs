namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The VANILLA (1.12) client mount — the third import lane, alongside TBC and WotLK.
///
/// It exists because a vanilla CLONE cannot be recolored. A clone reuses the source's own display,
/// which means the stock BLP and the stock M2 — and a weapon's colours live in the BLP while its
/// glow and particle colours live in the M2's additive passes and emitters. Touch either and it is
/// no longer a clone. Routing stock weapons through the import pipeline instead gives them the same
/// treatment TBC and WotLK weapons already get: recolor skin, tint glow / flame effects, enchant
/// visual, brightness and saturation. The cost is that it packages — patch and client restart —
/// where the clone lane is free. Both lanes stay, because they answer different questions.
///
/// TWO THINGS MAKE THIS LANE DIFFERENT FROM THE OTHER TWO.
///
/// First, the mount is the LIVE client — the same Data folder this app deploys into. Reading source
/// art out of a folder we also write to is a feedback loop: import a recolored sword and the next
/// import of that sword would recolor OUR recolor, compounding every pass. The repository's stock
/// ceiling is patch-2; patch-3 and above are app/custom output and are excluded even when this lane
/// falls back to the live client folder.
///
/// Second, the source geometry is ALREADY vanilla v256, so the import pipeline's re-emission is a
/// no-op conversion rather than a downgrade — the mesh does not have to survive a v260+ → v256
/// rewrite. What the lane is really buying here is the recolor and glow-tint bakes, not a format
/// change.
/// </summary>
public sealed class VanillaMpqSource : LegacyMpqSource
{
    /// <summary>Dedicated key first so the lane can point at a pristine copy of the client, falling
    /// back to the live client this app deploys into.</summary>
    public const string ConfigKey = "WeaponForge:VanillaDataPath";
    public const string FallbackConfigKey = "Vmangos:ClientDataPath";
    public const string SourceKey = "vanilla";
    public const string SourceLabel = "Vanilla (1.12)";

    private readonly IConfiguration _cfg;

    // Exact 1.12 Data archive set. An unknown MPQ is not stock merely because its filename does not
    // parse as patch-N; that mistake admitted patch-custom-*, staging archives and renamed backups.
    private static readonly HashSet<string> StockArchiveStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "backup", "base", "dbc", "fonts", "interface", "misc", "model",
        "sound", "speech", "terrain", "texture", "wmo", "patch", "patch-2",
    };

    public VanillaMpqSource(IConfiguration config, ILogger<VanillaMpqSource> logger)
        : base(config, logger, ConfigKey, SourceKey, SourceLabel, "VanillaMpq")
    {
        _cfg = config;
    }

    /// <summary>Unlike the other lanes this one needs no separate client install: with no dedicated
    /// path configured it mounts the client the app already knows about.</summary>
    public override string? ConfiguredPath
    {
        get
        {
            var p = _cfg[ConfigKey];
            if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            p = _cfg[FallbackConfigKey] ?? _cfg["SpellCreator:ClientDataPath"];
            return string.IsNullOrWhiteSpace(p) ? null : p.Trim();
        }
    }

    /// <summary>Exclude app/custom output from a source mount. Vanilla stock data in this project is
    /// defined as base archives plus bare patch and patch-2; patch-3 is the spell builder's output,
    /// patch-4 is retextures, and higher patches are forge/unified outputs.</summary>
    protected override bool ShouldMountArchive(string archiveFileName) => IsStockArchive(archiveFileName);

    internal static bool IsStockArchive(string archiveFileName)
        => StockArchiveStems.Contains(Path.GetFileNameWithoutExtension(archiveFileName));
}
