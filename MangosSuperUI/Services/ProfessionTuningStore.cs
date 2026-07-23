using System.Data;
using Dapper;

namespace MangosSuperUI.Services;

// ══════════════════════════════════════════════════════════════════════════
//  ProfessionTuningStore — tracking + reversibility for Profession Tuning.
//
//  STATIC, NO DI, takes an already-open IDbConnection — same pattern as
//  ItemSourceResolver. That matters here: PatchBuilderService consumes the
//  overrides during a patch-3 rebuild, and a static helper means no lifetime
//  mismatch (PatchBuilderService vs a scoped store) to reason about.
//
//  WHY A SNAPSHOT TABLE AT ALL
//  spell_template is MyISAM — no transactions, so a batch reduction cannot be
//  rolled back by SQL. Every touched recipe's ORIGINAL reagentCount1..8 is
//  snapshotted here before the first write, which buys three things:
//    1. Rollback (per-recipe and global) restores exact original counts.
//    2. Idempotency — a reduction is always computed off orig_counts, never off
//       the current value, so applying -25% twice is still -25%, not -44%.
//    3. The client override set. patch-3's Spell.dbc is rebuilt from a CLEAN
//       base every time, so the rows here are the ONLY record of what the
//       client's reagent counts should be. Drop a row → next rebuild is
//       pristine for that recipe. That is exactly what restore means.
//
//  Counts are stored as an 8-slot CSV ("4,2,0,0,0,0,0,0"). Slot i is
//  reagentCount{i+1}; a 0 means that reagent slot is empty and is never
//  touched.
// ══════════════════════════════════════════════════════════════════════════

public static class ProfessionTuningStore
{
    public const string Table = "profession_tuning";

    /// <summary>Client build the server resolves spells for (1.12.1).</summary>
    public const uint ClientBuild = 5875;

    /// <summary>
    /// Row shape for the client-override read.
    ///
    /// TYPED ON PURPOSE. Untyped Dapper.QueryAsync yields `dynamic`, and dynamic
    /// member access here binds at runtime — inside the try/catch below that would
    /// fail SILENTLY and hand back an empty override map, so patch-3 would quietly
    /// ship pristine reagent counts and the tuning would look like it did nothing.
    /// Keep this typed.
    /// </summary>
    private sealed class OverrideRow
    {
        public uint spell_entry { get; set; }
        public string? tuned_counts { get; set; }
    }

    public sealed class TuningRow
    {
        public uint SpellEntry { get; set; }
        public uint Build { get; set; }          // the EFFECTIVE build row that was written
        public uint SkillLine { get; set; }
        public int Pct { get; set; }
        public string OrigCounts { get; set; } = "";
        public string TunedCounts { get; set; } = "";
        public string SpellName { get; set; } = "";

        public uint[] Orig => ParseCounts(OrigCounts);
        public uint[] Tuned => ParseCounts(TunedCounts);
    }

    // ── DDL ──────────────────────────────────────────────────────────────
    // Called on first use. DbInitializationService does startup DDL for
    // app-owned tables; this self-provisions the same way WikiIndexer does so
    // the feature works without a migration step.
    public static async Task EnsureTableAsync(IDbConnection admin)
    {
        await admin.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS `{Table}` (
                `spell_entry`  INT UNSIGNED    NOT NULL,
                `build`        SMALLINT UNSIGNED NOT NULL DEFAULT 5875,
                `skill_line`   INT UNSIGNED    NOT NULL DEFAULT 0,
                `pct`          INT             NOT NULL DEFAULT 0,
                `orig_counts`  VARCHAR(64)     NOT NULL DEFAULT '',
                `tuned_counts` VARCHAR(64)     NOT NULL DEFAULT '',
                `spell_name`   VARCHAR(256)    NOT NULL DEFAULT '',
                `updated_at`   TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP
                                               ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (`spell_entry`),
                KEY `idx_skill` (`skill_line`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
    }

    // ── Reads ────────────────────────────────────────────────────────────

    public static async Task<List<TuningRow>> GetAllAsync(IDbConnection admin)
    {
        await EnsureTableAsync(admin);
        var rows = await admin.QueryAsync<TuningRow>($@"
            SELECT spell_entry AS SpellEntry, build AS Build, skill_line AS SkillLine,
                   pct AS Pct, orig_counts AS OrigCounts, tuned_counts AS TunedCounts,
                   spell_name AS SpellName
            FROM `{Table}` ORDER BY skill_line, spell_entry");
        return rows.ToList();
    }

    public static async Task<TuningRow?> GetAsync(IDbConnection admin, uint spellEntry)
    {
        await EnsureTableAsync(admin);
        return await admin.QueryFirstOrDefaultAsync<TuningRow>($@"
            SELECT spell_entry AS SpellEntry, build AS Build, skill_line AS SkillLine,
                   pct AS Pct, orig_counts AS OrigCounts, tuned_counts AS TunedCounts,
                   spell_name AS SpellName
            FROM `{Table}` WHERE spell_entry = @E", new { E = spellEntry });
    }

    public static async Task<Dictionary<uint, TuningRow>> GetForSkillAsync(IDbConnection admin, uint skillLine)
    {
        await EnsureTableAsync(admin);
        var rows = await admin.QueryAsync<TuningRow>($@"
            SELECT spell_entry AS SpellEntry, build AS Build, skill_line AS SkillLine,
                   pct AS Pct, orig_counts AS OrigCounts, tuned_counts AS TunedCounts,
                   spell_name AS SpellName
            FROM `{Table}` WHERE skill_line = @S", new { S = skillLine });
        return rows.ToDictionary(r => r.SpellEntry);
    }

    /// <summary>
    /// spellEntry → the 8 reagent counts the CLIENT should see. This is what
    /// PatchBuilderService applies to patch-3's Spell.dbc. An entry absent from
    /// this map is left pristine, which is precisely how restore works.
    /// </summary>
    public static async Task<Dictionary<uint, uint[]>> GetReagentOverridesAsync(IDbConnection admin)
    {
        try
        {
            await EnsureTableAsync(admin);
            var rows = await admin.QueryAsync<OverrideRow>(
                $"SELECT spell_entry, tuned_counts FROM `{Table}`");
            var map = new Dictionary<uint, uint[]>();
            foreach (var r in rows)
                map[r.spell_entry] = ParseCounts(r.tuned_counts ?? "");
            return map;
        }
        catch
        {
            // Never let tuning break a spell patch rebuild.
            return new Dictionary<uint, uint[]>();
        }
    }

    // ── Writes ───────────────────────────────────────────────────────────

    /// <summary>
    /// Insert or update a recipe's tuning. orig is only written on INSERT — an
    /// existing row keeps its original snapshot forever, which is what makes
    /// re-applying a percentage non-compounding.
    /// </summary>
    public static async Task UpsertAsync(IDbConnection admin, uint spellEntry, uint build,
                                         uint skillLine, int pct, uint[] orig, uint[] tuned,
                                         string spellName)
    {
        await EnsureTableAsync(admin);
        await admin.ExecuteAsync($@"
            INSERT INTO `{Table}`
                (spell_entry, build, skill_line, pct, orig_counts, tuned_counts, spell_name)
            VALUES (@E, @B, @S, @P, @O, @T, @N)
            ON DUPLICATE KEY UPDATE
                build        = VALUES(build),
                skill_line   = VALUES(skill_line),
                pct          = VALUES(pct),
                tuned_counts = VALUES(tuned_counts),
                spell_name   = VALUES(spell_name)",
            new
            {
                E = spellEntry,
                B = build,
                S = skillLine,
                P = pct,
                O = FormatCounts(orig),
                T = FormatCounts(tuned),
                N = spellName ?? ""
            });
    }

    public static async Task DeleteAsync(IDbConnection admin, uint spellEntry)
    {
        await EnsureTableAsync(admin);
        await admin.ExecuteAsync($"DELETE FROM `{Table}` WHERE spell_entry = @E", new { E = spellEntry });
    }

    public static async Task DeleteAllAsync(IDbConnection admin)
    {
        await EnsureTableAsync(admin);
        await admin.ExecuteAsync($"DELETE FROM `{Table}`");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static uint[] ParseCounts(string csv)
    {
        var outArr = new uint[8];
        if (string.IsNullOrWhiteSpace(csv)) return outArr;
        var parts = csv.Split(',');
        for (int i = 0; i < 8 && i < parts.Length; i++)
            uint.TryParse(parts[i].Trim(), out outArr[i]);
        return outArr;
    }

    public static string FormatCounts(uint[] counts)
    {
        var padded = new uint[8];
        for (int i = 0; i < 8 && i < counts.Length; i++) padded[i] = counts[i];
        return string.Join(",", padded);
    }

    /// <summary>
    /// The reduction rule. Empty reagent slots (reagent id 0) stay 0; a used
    /// slot floors at 1 so a recipe never becomes free. Always computed from
    /// the ORIGINAL counts.
    /// </summary>
    public static uint[] ApplyReduction(uint[] origCounts, uint[] reagentIds, int pct)
    {
        var outArr = new uint[8];
        double keep = 1.0 - (pct / 100.0);
        for (int i = 0; i < 8; i++)
        {
            if (i >= reagentIds.Length || reagentIds[i] == 0 || origCounts[i] == 0)
            {
                outArr[i] = 0;
                continue;
            }
            uint reduced = (uint)Math.Round(origCounts[i] * keep, MidpointRounding.AwayFromZero);
            outArr[i] = Math.Max(1u, reduced);
        }
        return outArr;
    }
}