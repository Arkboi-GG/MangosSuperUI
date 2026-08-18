using System.Security.Cryptography;
using System.Text;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The literal, hash-verified donor-2131 ("Shortsword") item_template fixture from WEAPON_GEN.md
/// §13.3 — the authoritative gameplay row the golden weapon clones. Embedded as constants (not read
/// from a live DB) so the generated item_template.sql is deterministic and cannot drift between
/// build time and owner apply time. GENERATED from the two tracked TSV lines in WEAPON_GEN.md; do
/// not hand-edit — regenerate if the fixture changes, and keep <see cref="Verify"/> passing.
/// </summary>
public static class DonorItemTemplateFixture
{
    public const string HeaderTsv = "entry\tpatch\tclass\tsubclass\tname\tdescription\tdisplay_id\tquality\tflags\tbuy_count\tbuy_price\tsell_price\tinventory_type\tallowable_class\tallowable_race\titem_level\trequired_level\trequired_skill\trequired_skill_rank\trequired_spell\trequired_honor_rank\trequired_city_rank\trequired_reputation_faction\trequired_reputation_rank\tmax_count\tstackable\tcontainer_slots\tstat_type1\tstat_value1\tstat_type2\tstat_value2\tstat_type3\tstat_value3\tstat_type4\tstat_value4\tstat_type5\tstat_value5\tstat_type6\tstat_value6\tstat_type7\tstat_value7\tstat_type8\tstat_value8\tstat_type9\tstat_value9\tstat_type10\tstat_value10\tdelay\trange_mod\tammo_type\tdmg_min1\tdmg_max1\tdmg_type1\tdmg_min2\tdmg_max2\tdmg_type2\tdmg_min3\tdmg_max3\tdmg_type3\tdmg_min4\tdmg_max4\tdmg_type4\tdmg_min5\tdmg_max5\tdmg_type5\tblock\tarmor\tholy_res\tfire_res\tnature_res\tfrost_res\tshadow_res\tarcane_res\tspellid_1\tspelltrigger_1\tspellcharges_1\tspellppmrate_1\tspellcooldown_1\tspellcategory_1\tspellcategorycooldown_1\tspellid_2\tspelltrigger_2\tspellcharges_2\tspellppmrate_2\tspellcooldown_2\tspellcategory_2\tspellcategorycooldown_2\tspellid_3\tspelltrigger_3\tspellcharges_3\tspellppmrate_3\tspellcooldown_3\tspellcategory_3\tspellcategorycooldown_3\tspellid_4\tspelltrigger_4\tspellcharges_4\tspellppmrate_4\tspellcooldown_4\tspellcategory_4\tspellcategorycooldown_4\tspellid_5\tspelltrigger_5\tspellcharges_5\tspellppmrate_5\tspellcooldown_5\tspellcategory_5\tspellcategorycooldown_5\tbonding\tpage_text\tpage_language\tpage_material\tstart_quest\tlock_id\tmaterial\tsheath\trandom_property\tset_id\tmax_durability\tarea_bound\tmap_bound\tduration\tbag_family\tdisenchant_id\tfood_type\tmin_money_loot\tmax_money_loot\twrapped_gift\textra_flags\tother_team_entry";
    public const string RowTsv = "2131\t0\t2\t7\tShortsword\t\t22075\t1\t0\t1\t54\t10\t13\t-1\t-1\t3\t1\t0\t0\t0\t0\t0\t0\t0\t0\t1\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t2600\t0\t0\t2\t4\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t-1\t0\t-1\t0\t0\t0\t0\t-1\t0\t-1\t0\t0\t0\t0\t-1\t0\t-1\t0\t0\t0\t0\t-1\t0\t-1\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t1\t3\t0\t0\t20\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t1";

    /// <summary>SHA-256 of (HeaderTsv + "\n" + RowTsv + "\n"), matching WEAPON_GEN.md §13.3.</summary>
    public const string ExpectedSha256 = "dfd89aacfc4704a05a58ccd5b570df76ad5f23171864f3c2275f243c4cc2477e";

    public const int DonorEntry = 2131;
    public const int DonorPatch = 0;
    public const int DonorDisplayId = 22075;

    public static string[] Columns => HeaderTsv.Split('\t');
    public static string[] DonorValues => RowTsv.Split('\t');

    /// <summary>Recompute the fixture hash and compare to the recorded value. Returns false if the
    /// embedded constants were corrupted — the SQL generator refuses to run when this fails.</summary>
    public static bool Verify()
    {
        var payload = Encoding.UTF8.GetBytes(HeaderTsv + "\n" + RowTsv + "\n");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return hash == ExpectedSha256 && Columns.Length == 130 && DonorValues.Length == 130;
    }
}
