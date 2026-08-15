using System.Text;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Builds the SQL postlude carried by an RTS world artifact. It never connects
/// to MySQL. World State applies the postlude only as part of Nico's explicit,
/// stopped-world restore ceremony; ordinary MMO artifacts are never rewritten.
/// </summary>
public static class RtsHeroSpellWorldStore
{
    public static readonly IReadOnlyList<int> ReservedSpellIds =
        Enumerable.Range(51001, 5).ToArray();

    public const string OriginalTable = "superui_rts_spell_original";
    public const string OriginalStateTable = "superui_rts_spell_original_state";

    /// <summary>
    /// R2 captures any pre-existing 51001..51005 rows once, then installs exactly
    /// those five native aura definitions. R1 restores the captured rows when its
    /// source previously ran R2; against an MMO source it is a true no-op.
    /// </summary>
    public static string BuildArtifactPostlude(WorldLaunchConfiguration input)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(input);
        if (!WorldConfigurationCatalog.IsR2(configuration))
            return BuildConditionalOriginalRestoreSql();

        var sql = new StringBuilder();
        sql.AppendLine("-- World State: preserve and install only the five RTS R2 hero aura rows.");
        sql.Append("CREATE TABLE IF NOT EXISTS `").Append(OriginalTable)
            .AppendLine("` LIKE `spell_template`;");
        sql.Append("CREATE TABLE IF NOT EXISTS `").Append(OriginalStateTable)
            .AppendLine("` (`id` TINYINT UNSIGNED NOT NULL PRIMARY KEY, `captured_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP);");
        sql.Append("INSERT IGNORE INTO `").Append(OriginalTable)
            .Append("` SELECT source.* FROM `spell_template` source WHERE source.`entry` IN (")
            .Append(ReservedIdList).Append(") AND NOT EXISTS (SELECT 1 FROM `")
            .Append(OriginalStateTable).AppendLine("` WHERE `id`=1);");
        sql.Append("INSERT IGNORE INTO `").Append(OriginalStateTable)
            .AppendLine("` (`id`) VALUES (1);");
        sql.Append("DELETE FROM `spell_template` WHERE `entry` IN (")
            .Append(ReservedIdList).AppendLine(");");
        sql.Append(BuildInsertRowsSql(configuration));
        return sql.ToString();
    }

    private static string BuildInsertRowsSql(WorldLaunchConfiguration configuration)
    {
        var sql = new StringBuilder();
        foreach (var rule in configuration.HeroRules.OrderBy(rule => rule.HeroLevel))
        {
            sql.AppendLine("INSERT INTO `spell_template`");
            sql.AppendLine("(`entry`,`build`,`school`,`attributes`,`targets`,`procFlags`,`procChance`,`procCharges`,`castingTimeIndex`,`durationIndex`,`rangeIndex`,`stackAmount`,`equippedItemClass`,`equippedItemSubClassMask`,`equippedItemInventoryTypeMask`,`effect1`,`effect2`,`effect3`,`effectBaseDice1`,`effectBaseDice2`,`effectDieSides1`,`effectDieSides2`,`effectBasePoints1`,`effectBasePoints2`,`effectImplicitTargetA1`,`effectImplicitTargetA2`,`effectImplicitTargetB1`,`effectImplicitTargetB2`,`effectApplyAuraName1`,`effectApplyAuraName2`,`effectMiscValue1`,`effectMiscValue2`,`effectAmplitude1`,`effectAmplitude2`,`effectTriggerSpell1`,`effectTriggerSpell2`,`customFlags`,`name`) VALUES");
            sql.Append('(').Append(rule.SpellId)
                .Append(",5875,0,0x80000040,0,0,0,0,1,21,1,1,-1,0,0,6,6,0,1,1,1,1,")
                .Append(rule.ScalePercent - 101).Append(',').Append(rule.DamagePercent - 101)
                .Append(",1,1,0,0,61,79,0,127,0,0,0,0,0,'RTS Hero Level ")
                .Append(rule.HeroLevel).AppendLine("');");
        }
        return sql.ToString();
    }

    private static string BuildConditionalOriginalRestoreSql()
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- World State: R1 restores pre-RTS rows when this source previously ran R2.");
        sql.Append("SET @ws_rts_has_original = IF((SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('")
            .Append(OriginalTable).Append("','").Append(OriginalStateTable)
            .AppendLine("'))=2,1,0);");
        sql.Append("SET @ws_sql = IF(@ws_rts_has_original=1, 'SELECT COUNT(*) INTO @ws_rts_original_ready FROM `")
            .Append(OriginalStateTable).AppendLine("` WHERE `id`=1', 'SET @ws_rts_original_ready=0');");
        sql.AppendLine("PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;");
        sql.Append("SET @ws_sql = IF(@ws_rts_original_ready=1, 'DELETE FROM `spell_template` WHERE `entry` IN (")
            .Append(ReservedIdList).AppendLine(")', 'SELECT 1');");
        sql.AppendLine("PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;");
        sql.Append("SET @ws_sql = IF(@ws_rts_original_ready=1, 'INSERT INTO `spell_template` SELECT * FROM `")
            .Append(OriginalTable).Append("` WHERE `entry` IN (").Append(ReservedIdList)
            .AppendLine(")', 'SELECT 1');");
        sql.AppendLine("PREPARE ws_stmt FROM @ws_sql; EXECUTE ws_stmt; DEALLOCATE PREPARE ws_stmt;");
        return sql.ToString();
    }

    private static string ReservedIdList => string.Join(',', ReservedSpellIds);
}
