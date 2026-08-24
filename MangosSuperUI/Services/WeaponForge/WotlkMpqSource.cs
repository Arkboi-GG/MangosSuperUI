namespace MangosSuperUI.Services.WeaponForge;

/// <summary>The WotLK (3.3.5a) client mount — <c>WeaponForge:WotlkDataPath</c>. Same reader, same
/// mount rules; models are v264 + <c>.skin</c> (see <see cref="LegacyMpqSource.LoadM2"/>).</summary>
public sealed class WotlkMpqSource : LegacyMpqSource
{
    public const string ConfigKey = "WeaponForge:WotlkDataPath";
    public const string SourceKey = "wotlk";
    public const string SourceLabel = "WotLK (3.3.5a)";

    public WotlkMpqSource(IConfiguration config, ILogger<WotlkMpqSource> logger)
        : base(config, logger, ConfigKey, SourceKey, SourceLabel, "WotlkMpq") { }
}
