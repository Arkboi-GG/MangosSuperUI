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
    /// Used only while creating an RTS World State. It adds the two preservation
    /// tables to the copied stock world schema, captures any pre-existing reserved
    /// rows once, and installs the five RTS hero auras.
    /// </summary>
    public static string BuildCreationArtifactPostlude(WorldLaunchConfiguration input)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(input);
        var sql = new StringBuilder();
        sql.AppendLine("-- World State RTS creation: add preservation tables and install the five hero aura rows.");
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
        sql.Append(BuildHeroAuraRefreshSql(configuration));
        return sql.ToString();
    }

    /// <summary>
    /// Used while resuming an RTS World State. The snapshot already contains the
    /// complete RTS overlay, so this postlude may update managed aura rows but must
    /// never create or alter schema.
    /// </summary>
    public static string BuildResumeArtifactPostlude(WorldLaunchConfiguration input)
    {
        var configuration = WorldConfigurationCatalog.NormalizeAndValidate(input);
        return "-- World State RTS resume: refresh managed hero aura rows; schema already exists.\n" +
            BuildHeroAuraRefreshSql(configuration);
    }

    private static string BuildHeroAuraRefreshSql(WorldLaunchConfiguration configuration)
    {
        var sql = new StringBuilder();
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

    private static string ReservedIdList => string.Join(',', ReservedSpellIds);
}
