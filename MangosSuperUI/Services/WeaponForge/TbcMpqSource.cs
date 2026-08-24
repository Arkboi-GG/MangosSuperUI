namespace MangosSuperUI.Services.WeaponForge;

/// <summary>The TBC (2.4.3) client mount — <c>WeaponForge:TbcDataPath</c>.</summary>
public sealed class TbcMpqSource : LegacyMpqSource
{
    public const string ConfigKey = "WeaponForge:TbcDataPath";
    public const string SourceKey = "tbc";
    public const string SourceLabel = "TBC (2.4.3)";

    public TbcMpqSource(IConfiguration config, ILogger<TbcMpqSource> logger)
        : base(config, logger, ConfigKey, SourceKey, SourceLabel, "TbcMpq") { }
}
