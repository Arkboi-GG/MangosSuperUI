using System.Text;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Builds a zero-roster RTS snapshot entirely from files in an eligible v2
/// snapshot. It never restores or edits one of the canonical live databases.
/// </summary>
public sealed class RtsWorldCreationService
{
    private readonly WorldArtifactService _artifacts;
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] AdminBotStateTables =
    {
        "bot_personality", "bot_activity_log", "bot_registry", "bot_wallet",
        "bot_inventory", "bot_groups", "bot_persona", "chat_log", "chat_relationship"
    };

    public RtsWorldCreationService(WorldArtifactService artifacts, IWebHostEnvironment environment)
    {
        _artifacts = artifacts;
        _environment = environment;
    }

    public async Task<RtsWorldBuildResult> BuildAsync(
        string sourceDirectory,
        string stagingDirectory,
        CreateRtsWorldRequestModel request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Give the RTS world a name.");

        RequireSource(sourceDirectory, WorldArtifactService.WorldMangos);
        RequireSource(sourceDirectory, WorldArtifactService.WorldAdmin);
        RequireSource(sourceDirectory, WorldArtifactService.PlayersCharacters);
        RequireSource(sourceDirectory, WorldArtifactService.PlayersRealmd);
        RequireSource(sourceDirectory, WorldArtifactService.PlayersCharactersSchema);
        RequireSource(sourceDirectory, WorldArtifactService.PlayersCharactersSystem);
        RequireSource(sourceDirectory, WorldArtifactService.CoreArchive);
        RequireSource(sourceDirectory, WorldArtifactService.CoreConfig);

        var sourceRealmId = await ReadSourceRealmIdAsync(sourceDirectory, cancellationToken);
        if (request.Configuration.RealmId != sourceRealmId)
            throw new InvalidOperationException(
                $"Realm ID is inherited from the source snapshot ({sourceRealmId}) and cannot be changed to {request.Configuration.RealmId}.");
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(request.Configuration);
        var namePool = await GetNamePoolStatsAsync(cancellationToken);
        WorldConfigurationCatalog.ValidateNamePoolCapacity(configuration, namePool.ValidUniqueNames);

        Directory.CreateDirectory(stagingDirectory);

        await _artifacts.TransformGzipAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.WorldMangos),
            Path.Combine(stagingDirectory, WorldArtifactService.WorldMangos),
            RtsHeroSpellWorldStore.BuildArtifactPostlude(configuration), cancellationToken);
        await _artifacts.TransformGzipAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.WorldAdmin),
            Path.Combine(stagingDirectory, WorldArtifactService.WorldAdmin),
            BuildAdminResetSql(), cancellationToken);

        var charactersDefinition = await _artifacts.InspectDatabaseDumpAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.PlayersCharacters),
            "characters", cancellationToken);
        if (charactersDefinition == null)
            throw new InvalidOperationException(
                "The selected source snapshot predates self-contained database artifacts. " +
                "Restore it and suspend it once with the updated World State system before creating an RTS campaign.");
        var charactersPreamble = WorldArtifactService.BuildDatabasePreamble(charactersDefinition);
        await _artifacts.ComposeGzipWithPreludeAsync(
            Path.Combine(stagingDirectory, WorldArtifactService.PlayersCharacters),
            new[]
            {
                Path.Combine(sourceDirectory, WorldArtifactService.PlayersCharactersSchema),
                Path.Combine(sourceDirectory, WorldArtifactService.PlayersCharactersSystem)
            },
            charactersPreamble,
            BuildCharactersSeedSql(configuration), cancellationToken);

        await _artifacts.TransformGzipAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.PlayersRealmd),
            Path.Combine(stagingDirectory, WorldArtifactService.PlayersRealmd),
            "UPDATE `realmcharacters` SET `numchars`=0;",
            cancellationToken);

        // Keep the clean template in every generated world so it can become a future campaign parent.
        await CopyAsync(sourceDirectory, stagingDirectory, WorldArtifactService.PlayersCharactersSchema, cancellationToken);
        await CopyAsync(sourceDirectory, stagingDirectory, WorldArtifactService.PlayersCharactersSystem, cancellationToken);
        await CopyAsync(sourceDirectory, stagingDirectory, WorldArtifactService.CoreArchive, cancellationToken);

        var config = await MangosdConfigDocument.LoadAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.CoreConfig), cancellationToken);
        config.ApplyWorldConfiguration(configuration);
        await File.WriteAllTextAsync(
            Path.Combine(stagingDirectory, WorldArtifactService.CoreConfig),
            config.ToString(), new UTF8Encoding(false), cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(stagingDirectory, WorldArtifactService.RotationAssignments),
            "{}\n", new UTF8Encoding(false), cancellationToken);

        // Validate every produced compressed artifact before the caller publishes the folder.
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.WorldMangos), cancellationToken);
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.WorldAdmin), cancellationToken);
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.PlayersCharacters), cancellationToken);
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.PlayersRealmd), cancellationToken);
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.PlayersCharactersSchema), cancellationToken);
        await _artifacts.ValidateGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.PlayersCharactersSystem), cancellationToken);
        await _artifacts.ValidateTarGzipAsync(Path.Combine(stagingDirectory, WorldArtifactService.CoreArchive), cancellationToken);

        return new RtsWorldBuildResult
        {
            Configuration = configuration,
            NamePoolEligible = namePool.ValidUniqueNames,
            NamePoolSha256 = namePool.Sha256
        };
    }

    private async Task CopyAsync(string source, string destination, string file, CancellationToken cancellationToken)
    {
        var input = Path.Combine(source, file);
        var output = Path.Combine(destination, file);
        await using var inputStream = File.OpenRead(input);
        await using var outputStream = File.Create(output);
        await inputStream.CopyToAsync(outputStream, cancellationToken);
    }

    private static void RequireSource(string directory, string file)
    {
        if (!File.Exists(Path.Combine(directory, file)))
            throw new InvalidOperationException(
                $"The selected snapshot is not eligible for a clean RTS campaign: '{file}' is missing. " +
                "Restore the MMO snapshot and suspend it once with the updated World State system first.");
    }

    public async Task<int> ReadSourceRealmIdAsync(
        string sourceDirectory, CancellationToken cancellationToken = default)
    {
        RequireSource(sourceDirectory, WorldArtifactService.CoreConfig);
        var config = await MangosdConfigDocument.LoadAsync(
            Path.Combine(sourceDirectory, WorldArtifactService.CoreConfig), cancellationToken);
        var realmId = config.GetInt("RealmID");
        if (realmId is null or <= 0)
            throw new InvalidDataException(
                $"{WorldArtifactService.CoreConfig} must contain one positive RealmID value.");
        return realmId.Value;
    }

    public async Task<BotNamePoolStats> GetNamePoolStatsAsync(CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _environment.WebRootPath;
        var path = Path.Combine(root, "data", "wow_era_5000_names.txt");
        if (!File.Exists(path)) throw new InvalidOperationException("The WoW-era bot name list is missing.");
        var names = (await File.ReadAllLinesAsync(path, cancellationToken))
            .Select(x => x.Trim())
            .Where(x => x.Length is >= 2 and <= 12 && x.All(char.IsLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new BotNamePoolStats
        {
            ValidUniqueNames = names,
            Sha256 = await WorldArtifactService.Sha256Async(path, cancellationToken)
        };
    }

    private static string BuildAdminResetSql()
    {
        var sql = new StringBuilder("\n-- World State: reset campaign-local bot identity/state.\n");
        foreach (var table in AdminBotStateTables)
        {
            sql.Append("SET @ws_sql = IF(EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='")
                .Append(table)
                .Append("'), 'DELETE FROM `")
                .Append(table)
                .Append("`', 'SELECT 1'); PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;\n");
        }
        sql.Append("SET @ws_sql = IF(EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='bot_settings'), ")
            .Append("'DELETE FROM `bot_settings` WHERE `setting_key` IN (''trace:enabled'',''trace:guids'')', 'SELECT 1'); ")
            .AppendLine("PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;");
        return sql.ToString();
    }

    public static string BuildCharactersSeedSql(WorldLaunchConfiguration input)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(input);
        var rows = WorldConfigurationCatalog.ToWorldStateRows(configuration);
        var heroRules = WorldConfigurationCatalog.ToHeroRuleRows(configuration);
        var sql = new StringBuilder();
        sql.Append("-- World State: clean ").Append(configuration.ProfileId).AppendLine(" genesis.");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_worldstate` (`key` VARCHAR(32) NOT NULL PRIMARY KEY, `value` VARCHAR(64) NOT NULL);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_rules_zone` (`zone_id` INT UNSIGNED NOT NULL PRIMARY KEY, `ore` TINYINT UNSIGNED NOT NULL DEFAULT 0, `skins` TINYINT UNSIGNED NOT NULL DEFAULT 0, `herbs` TINYINT UNSIGNED NOT NULL DEFAULT 0);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_rules_hub` (`hub_id` SMALLINT UNSIGNED NOT NULL PRIMARY KEY, `zone_id` INT UNSIGNED NOT NULL, `name` VARCHAR(64) NOT NULL, `banner_go_guid` INT UNSIGNED NOT NULL, `event_alliance` SMALLINT UNSIGNED NOT NULL, `event_horde` SMALLINT UNSIGNED NOT NULL, `capture_ms` INT UNSIGNED NOT NULL DEFAULT 60000, `initial_controller` TINYINT UNSIGNED NOT NULL DEFAULT 0);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_rules_hero` (`hero_level` TINYINT UNSIGNED NOT NULL PRIMARY KEY, `declare_cost` INT UNSIGNED NOT NULL, `revive_fee` INT UNSIGNED NOT NULL, `spell_id` INT UNSIGNED NOT NULL, `scale_percent` SMALLINT UNSIGNED NOT NULL DEFAULT 100, `damage_percent` SMALLINT UNSIGNED NOT NULL DEFAULT 100);");
        AppendEnsureHeroRuleColumn(sql, "scale_percent", "SMALLINT UNSIGNED NOT NULL DEFAULT 100");
        AppendEnsureHeroRuleColumn(sql, "damage_percent", "SMALLINT UNSIGNED NOT NULL DEFAULT 100");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_rules_dungeon` (`map_id` INT UNSIGNED NOT NULL PRIMARY KEY, `final_boss_entry` INT UNSIGNED NOT NULL, `buff_spell_id` INT UNSIGNED NOT NULL, `loot_items` TINYINT UNSIGNED NOT NULL DEFAULT 10);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_faction` (`team` TINYINT UNSIGNED NOT NULL PRIMARY KEY, `honor_pool` BIGINT NOT NULL DEFAULT 0);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_heroes` (`guid` INT UNSIGNED NOT NULL PRIMARY KEY, `team` TINYINT UNSIGNED NOT NULL, `hero_level` TINYINT UNSIGNED NOT NULL DEFAULT 1, `dead` TINYINT UNSIGNED NOT NULL DEFAULT 0, `declared_at` BIGINT UNSIGNED NOT NULL DEFAULT 0);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_zone_control` (`zone_id` INT UNSIGNED NOT NULL PRIMARY KEY, `controller` TINYINT UNSIGNED NOT NULL DEFAULT 0);");
        sql.AppendLine("CREATE TABLE IF NOT EXISTS `superui_dungeon_control` (`map_id` INT UNSIGNED NOT NULL PRIMARY KEY, `controller` TINYINT UNSIGNED NOT NULL DEFAULT 0);");
        sql.AppendLine("DELETE FROM `superui_worldstate` WHERE `key`='mode' OR `key`='state.flush_ms' OR `key` LIKE 'rate.%' OR `key` LIKE 'bots.cap.%' OR `key` LIKE 'honor.weight.%' OR `key` IN ('honor.enabled','honor.suppress_bot_hk','control.faction_bots','hero.enabled','hero.slots_fixed');");
        foreach (var row in rows)
            sql.Append("INSERT INTO `superui_worldstate` (`key`,`value`) VALUES ('")
                .Append(SqlLiteral(row.Key)).Append("','").Append(SqlLiteral(row.Value))
                .AppendLine("') ON DUPLICATE KEY UPDATE `value`=VALUES(`value`);");
        sql.AppendLine("DELETE FROM `superui_rules_zone`; DELETE FROM `superui_rules_hub`; DELETE FROM `superui_rules_hero`; DELETE FROM `superui_rules_dungeon`;");
        foreach (var rule in heroRules)
        {
            sql.Append("INSERT INTO `superui_rules_hero` (`hero_level`,`declare_cost`,`revive_fee`,`spell_id`,`scale_percent`,`damage_percent`) VALUES (")
                .Append(rule.HeroLevel).Append(',').Append(rule.HonorCost).Append(',')
                .Append(rule.ReviveFee).Append(',').Append(rule.SpellId).Append(',')
                .Append(rule.ScalePercent).Append(',').Append(rule.DamagePercent)
                .AppendLine(");");
        }
        sql.AppendLine("DELETE FROM `superui_heroes`; DELETE FROM `superui_zone_control`; DELETE FROM `superui_dungeon_control`; DELETE FROM `superui_faction`;");
        sql.AppendLine("INSERT INTO `superui_faction` (`team`,`honor_pool`) VALUES (0,0),(1,0);");
        return sql.ToString();
    }

    private static void AppendEnsureHeroRuleColumn(StringBuilder sql, string column, string definition)
    {
        sql.Append("SET @ws_sql = IF(EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='superui_rules_hero' AND column_name='")
            .Append(column)
            .Append("'), 'SELECT 1', 'ALTER TABLE `superui_rules_hero` ADD COLUMN `")
            .Append(column).Append("` ").Append(definition)
            .AppendLine("'); PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;");
    }

    private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class RtsWorldBuildResult
{
    public WorldLaunchConfiguration Configuration { get; set; } = new();
    public int NamePoolEligible { get; set; }
    public string NamePoolSha256 { get; set; } = "";
}

public sealed class BotNamePoolStats
{
    public int ValidUniqueNames { get; set; }
    public string Sha256 { get; set; } = "";
}
