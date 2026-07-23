using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Models;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ══════════════════════════════════════════════════════════════════════════
//  ProfessionTuningController — batch-reduce reagent costs across a whole
//  profession. Modeled on the Crafting Lootifier's Professions tab, but this
//  tool does NOT lootify: it never creates item_template rows.
//
//  ── THE BUILD TRAP (read before touching the SQL) ────────────────────────
//  spell_template's PK is (entry, build) and the table holds ELEVEN build
//  layers. VMaNGOS loads each spell from its highest build <= 5875. For real
//  gear recipes that layer is almost always 4222:
//
//      effective build   recipes
//      4222              557
//      4297..5464        151
//      5875              5      <-- what a naive "WHERE build = 5875" would hit
//
//  So every write resolves the effective build per recipe first (the JOIN on
//  MAX(build) <= 5875 below) and keys the UPDATE on (entry, effBuild). Note
//  MariaDB/MyISAM will not let you UPDATE a table you also SELECT from, which
//  is why the resolve happens in a separate read pass rather than a subquery.
//
//  ── SERVER + CLIENT ──────────────────────────────────────────────────────
//  The server reads reagents from spell_template (this controller writes it).
//  The 1.12.1 CLIENT reads reagent counts from Spell.dbc for the tradeskill
//  tooltip AND for the client-side "Create" gate — so a server-only change is
//  invisible and the client keeps blocking at the old count. The client half
//  rides the existing patch-3 rebuild (Spell.dbc field 50+i = ReagentCount[i],
//  which sits directly below the confirmed Effect[0] @ 61). Apply/Restore/
//  RollbackAll therefore return needsClientRebuild, and the JS chains
//  POST /Patch/RebuildClientPatch.
//
//  Do NOT give this tool its own MPQ. patch-3 already carries Spell.dbc for
//  the Spell Creator; a separate higher-numbered patch would override it
//  wholesale and silently drop every custom spell in-client.
// ══════════════════════════════════════════════════════════════════════════

public class ProfessionTuningController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly AuditService _audit;
    private readonly ILogger<ProfessionTuningController> _logger;

    public ProfessionTuningController(
        ConnectionFactory db,
        DbcService dbc,
        AuditService audit,
        ILogger<ProfessionTuningController> logger)
    {
        _db = db;
        _dbc = dbc;
        _audit = audit;
        _logger = logger;
    }

    private const uint CLIENT_BUILD = ProfessionTuningStore.ClientBuild; // 5875

    // Professions with reagent-consuming recipes. DbcService.GetProfessions()
    // deliberately lists only the four GEAR-making skills (it exists for the
    // Crafting Lootifier, which needs equippable output), so this tool keeps
    // its own wider list rather than widening a shared surface other callers
    // depend on.
    private static readonly (uint id, string name)[] TUNABLE_PROFESSIONS =
    {
        (164u, "Blacksmithing"),
        (165u, "Leatherworking"),
        (171u, "Alchemy"),
        (197u, "Tailoring"),
        (202u, "Engineering"),
        (333u, "Enchanting"),
        (185u, "Cooking"),
        (129u, "First Aid"),
        (186u, "Mining (Smelting)"),
        (40u,  "Poisons"),
    };

    public IActionResult Index() => View();

    // ===================== META =====================

    [HttpGet]
    public IActionResult Meta() => Json(new { defaultPct = 25, clientBuild = CLIENT_BUILD });

    // ===================== PROFESSIONS =====================

    [HttpGet]
    public async Task<IActionResult> Professions()
    {
        using var mangos = _db.Mangos();
        using var admin = _db.Admin();

        var tunedBySkill = (await ProfessionTuningStore.GetAllAsync(admin))
            .GroupBy(r => r.SkillLine)
            .ToDictionary(g => g.Key, g => g.Count());

        var outList = new List<object>();
        foreach (var (id, name) in TUNABLE_PROFESSIONS)
        {
            int total;
            try
            {
                var recipes = await LoadRecipeRowsAsync(mangos, id);
                total = recipes.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProfessionTuning: recipe count failed for skill {S}", id);
                total = 0;
            }

            outList.Add(new
            {
                id,
                name,
                totalRecipes = total,
                tunedRecipes = tunedBySkill.TryGetValue(id, out var t) ? t : 0
            });
        }

        return Json(new { professions = outList });
    }

    // ===================== RECIPES =====================

    /// <summary>
    /// One reagent-consuming recipe at its EFFECTIVE build, with reagents
    /// resolved to item names/icons and its current tuning state.
    /// </summary>
    private sealed class RecipeRow
    {
        public uint entry { get; set; }
        public uint build { get; set; }
        public string name { get; set; } = "";
        public ulong effectItemType1 { get; set; }
        public int reagent1 { get; set; }
        public int reagent2 { get; set; }
        public int reagent3 { get; set; }
        public int reagent4 { get; set; }
        public int reagent5 { get; set; }
        public int reagent6 { get; set; }
        public int reagent7 { get; set; }
        public int reagent8 { get; set; }
        public uint reagentCount1 { get; set; }
        public uint reagentCount2 { get; set; }
        public uint reagentCount3 { get; set; }
        public uint reagentCount4 { get; set; }
        public uint reagentCount5 { get; set; }
        public uint reagentCount6 { get; set; }
        public uint reagentCount7 { get; set; }
        public uint reagentCount8 { get; set; }

        public uint[] ReagentIds => new[]
        {
            (uint)Math.Max(0, reagent1), (uint)Math.Max(0, reagent2),
            (uint)Math.Max(0, reagent3), (uint)Math.Max(0, reagent4),
            (uint)Math.Max(0, reagent5), (uint)Math.Max(0, reagent6),
            (uint)Math.Max(0, reagent7), (uint)Math.Max(0, reagent8)
        };

        public uint[] Counts => new[]
        {
            reagentCount1, reagentCount2, reagentCount3, reagentCount4,
            reagentCount5, reagentCount6, reagentCount7, reagentCount8
        };
    }

    /// <summary>
    /// Recipe spells for a skill line, each at its effective build (highest
    /// build &lt;= 5875), filtered to those that actually consume reagents.
    /// </summary>
    private async Task<List<RecipeRow>> LoadRecipeRowsAsync(System.Data.IDbConnection mangos, uint skillLineId)
    {
        var spellIds = _dbc.GetProfessionRecipeSpells(skillLineId)
            .Select(r => r.spell)
            .Distinct()
            .ToList();

        if (spellIds.Count == 0) return new List<RecipeRow>();

        // The inner MAX(build) join is the whole point — see the build trap note.
        var rows = await mangos.QueryAsync<RecipeRow>(@"
            SELECT st.entry, st.build, st.name, st.effectItemType1,
                   st.reagent1, st.reagent2, st.reagent3, st.reagent4,
                   st.reagent5, st.reagent6, st.reagent7, st.reagent8,
                   st.reagentCount1, st.reagentCount2, st.reagentCount3, st.reagentCount4,
                   st.reagentCount5, st.reagentCount6, st.reagentCount7, st.reagentCount8
            FROM spell_template st
            JOIN (
                SELECT entry, MAX(build) AS eff
                FROM spell_template
                WHERE entry IN @Ids AND build <= @Build
                GROUP BY entry
            ) m ON m.entry = st.entry AND m.eff = st.build
            WHERE st.reagent1 > 0
            ORDER BY st.name",
            new { Ids = spellIds, Build = CLIENT_BUILD });

        return rows.ToList();
    }

    /// <summary>
    /// Reagent / output item lookup row.
    ///
    /// TYPED ON PURPOSE. The untyped Dapper.QueryAsync returns `dynamic` rows, and
    /// building a (string, uint) tuple out of dynamic members infers
    /// ValueTuple&lt;string, object&gt; at compile time — which then fails at RUNTIME
    /// with RuntimeBinderException ("Cannot implicitly convert
    /// ValueTuple&lt;string,object&gt; to ValueTuple&lt;string,uint&gt;"). It compiles
    /// clean, so the only way to catch it is to hit the endpoint. Keep this typed.
    /// </summary>
    private sealed class ItemLookupRow
    {
        public uint entry { get; set; }
        public string? name { get; set; }
        public uint display_id { get; set; }
        public uint quality { get; set; }   // 0 poor .. 6 artifact — drives the WoW name colour
    }

    [HttpGet]
    public async Task<IActionResult> ProfessionRecipes(uint skillLineId)
    {
        using var mangos = _db.Mangos();
        using var admin = _db.Admin();

        var recipes = await LoadRecipeRowsAsync(mangos, skillLineId);
        var tuned = await ProfessionTuningStore.GetForSkillAsync(admin, skillLineId);

        // Required skill level per recipe, for the "skill level" sort. It lives in
        // SkillLineAbility.dbc (minRank) — LoadRecipeRowsAsync only needs the spell
        // ids so it discards the rank. A spell can appear more than once on a skill
        // line, so keep the LOWEST rank (the point it first becomes learnable).
        var rankBySpell = new Dictionary<uint, uint>();
        foreach (var (spell, minRank) in _dbc.GetProfessionRecipeSpells(skillLineId))
        {
            if (!rankBySpell.TryGetValue(spell, out var existing) || minRank < existing)
                rankBySpell[spell] = minRank;
        }

        // Resolve every reagent + output item in one round trip.
        var itemIds = new HashSet<uint>();
        foreach (var r in recipes)
        {
            foreach (var id in r.ReagentIds) if (id != 0) itemIds.Add(id);
            if (r.effectItemType1 != 0) itemIds.Add((uint)r.effectItemType1);
        }

        var itemInfo = new Dictionary<uint, ItemLookupRow>();
        if (itemIds.Count > 0)
        {
            // NB: MySQL/MariaDB column names are case-insensitive, so `quality`
            // matches whatever casing item_template actually declares.
            var items = await mangos.QueryAsync<ItemLookupRow>(
                "SELECT entry, name, display_id, quality FROM item_template WHERE entry IN @Ids",
                new { Ids = itemIds.ToList() });
            foreach (var it in items)
                itemInfo[it.entry] = it;
        }

        string? IconFor(uint itemEntry)
        {
            if (itemEntry == 0 || !itemInfo.TryGetValue(itemEntry, out var info)) return null;
            try { return _dbc.GetItemIconPath(info.display_id); }
            catch { return null; }
        }

        string NameFor(uint itemEntry) =>
            itemInfo.TryGetValue(itemEntry, out var info) && !string.IsNullOrEmpty(info.name)
                ? info.name!
                : $"Item #{itemEntry}";

        uint QualityFor(uint itemEntry) =>
            itemInfo.TryGetValue(itemEntry, out var info) ? info.quality : 1u;

        var outRecipes = new List<object>();
        foreach (var r in recipes)
        {
            tuned.TryGetValue(r.entry, out var t);
            var orig = t?.Orig;
            var ids = r.ReagentIds;
            var counts = r.Counts;

            var reagents = new List<object>();
            for (int i = 0; i < 8; i++)
            {
                if (ids[i] == 0 || counts[i] == 0) continue;
                reagents.Add(new
                {
                    itemEntry = ids[i],
                    name = NameFor(ids[i]),
                    iconPath = IconFor(ids[i]),
                    quality = QualityFor(ids[i]),
                    count = counts[i],
                    origCount = orig != null && orig[i] != 0 ? (uint?)orig[i] : null
                });
            }

            outRecipes.Add(new
            {
                spellEntry = r.entry,
                effBuild = r.build,
                name = string.IsNullOrWhiteSpace(r.name) ? $"Spell #{r.entry}" : r.name,
                iconPath = IconFor((uint)r.effectItemType1),
                quality = QualityFor((uint)r.effectItemType1),
                minRank = rankBySpell.TryGetValue(r.entry, out var mr) ? mr : 0u,
                currentPct = t?.Pct ?? 0,
                reagents
            });
        }

        return Json(new
        {
            name = TUNABLE_PROFESSIONS.FirstOrDefault(p => p.id == skillLineId).name
                   ?? $"Skill {skillLineId}",
            recipes = outRecipes
        });
    }

    // ===================== APPLY =====================

    public class ApplyRequest
    {
        public uint SkillLineId { get; set; }
        public double Pct { get; set; }
        public List<uint> SpellEntries { get; set; } = new();
    }

    [HttpPost]
    public async Task<IActionResult> Apply([FromBody] ApplyRequest req)
    {
        if (req == null || req.SpellEntries == null || req.SpellEntries.Count == 0)
            return Json(new { success = false, error = "No recipes selected." });
        if (req.Pct <= 0 || req.Pct > 90)
            return Json(new { success = false, error = "Reduction must be between 1 and 90%." });

        int pct = (int)Math.Round(req.Pct);

        try
        {
            using var mangos = _db.Mangos();
            using var admin = _db.Admin();

            var all = await LoadRecipeRowsAsync(mangos, req.SkillLineId);
            var wanted = new HashSet<uint>(req.SpellEntries);
            var targets = all.Where(r => wanted.Contains(r.entry)).ToList();

            var existing = await ProfessionTuningStore.GetForSkillAsync(admin, req.SkillLineId);

            int tunedCount = 0;
            foreach (var r in targets)
            {
                var ids = r.ReagentIds;

                // Originals come from the snapshot if we've touched this recipe
                // before, otherwise from what's in the DB right now (which IS
                // the original on first touch). This is what stops -25% applied
                // twice from becoming -44%.
                uint[] orig = existing.TryGetValue(r.entry, out var prior) && prior.OrigCounts.Length > 0
                    ? prior.Orig
                    : r.Counts;

                var tuned = ProfessionTuningStore.ApplyReduction(orig, ids, pct);

                await mangos.ExecuteAsync(@"
                    UPDATE spell_template SET
                        reagentCount1 = @C1, reagentCount2 = @C2,
                        reagentCount3 = @C3, reagentCount4 = @C4,
                        reagentCount5 = @C5, reagentCount6 = @C6,
                        reagentCount7 = @C7, reagentCount8 = @C8
                    WHERE entry = @E AND build = @B",
                    new
                    {
                        C1 = tuned[0],
                        C2 = tuned[1],
                        C3 = tuned[2],
                        C4 = tuned[3],
                        C5 = tuned[4],
                        C6 = tuned[5],
                        C7 = tuned[6],
                        C8 = tuned[7],
                        E = r.entry,
                        B = r.build
                    });

                await ProfessionTuningStore.UpsertAsync(
                    admin, r.entry, r.build, req.SkillLineId, pct, orig, tuned, r.name);

                tunedCount++;
            }

            string profName = TUNABLE_PROFESSIONS.FirstOrDefault(p => p.id == req.SkillLineId).name
                              ?? $"Skill {req.SkillLineId}";
            await AuditAsync(
                "profession_tuning_apply",
                profName,
                new { skillLineId = req.SkillLineId, pct, recipesTuned = tunedCount },
                reversible: true,
                $"Profession Tuning ({profName}): -{pct}% reagents on {tunedCount} recipe(s)");

            _logger.LogInformation(
                "ProfessionTuning: -{Pct}% applied to {N} recipes in skill {S}",
                pct, tunedCount, req.SkillLineId);

            return Json(new
            {
                success = true,
                recipesTuned = tunedCount,
                itemsPatched = tunedCount,
                needsClientRebuild = true,
                mpqPath = "patch-3.MPQ",
                note = "spell_template updated. Restart mangosd for the server to pick up the new reagent counts."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProfessionTuning: Apply failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ===================== RESTORE ONE =====================

    public class RestoreRequest { public uint SpellEntry { get; set; } }

    [HttpPost]
    public async Task<IActionResult> RestoreRecipe([FromBody] RestoreRequest req)
    {
        if (req == null || req.SpellEntry == 0)
            return Json(new { success = false, error = "No recipe given." });

        try
        {
            using var mangos = _db.Mangos();
            using var admin = _db.Admin();

            var row = await ProfessionTuningStore.GetAsync(admin, req.SpellEntry);
            if (row == null)
                return Json(new { success = false, error = "That recipe is not tuned." });

            await RestoreOneAsync(mangos, admin, row);
            await AuditAsync(
                "profession_tuning_restore",
                string.IsNullOrWhiteSpace(row.SpellName) ? $"Spell #{row.SpellEntry}" : row.SpellName,
                new { spellEntry = row.SpellEntry, wasPct = row.Pct },
                reversible: false,
                $"Profession Tuning: restored recipe #{row.SpellEntry} to original reagent counts (was -{row.Pct}%)");

            return Json(new { success = true, needsClientRebuild = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProfessionTuning: Restore failed for {E}", req.SpellEntry);
            return Json(new { success = false, error = ex.Message });
        }
    }

    private static async Task RestoreOneAsync(System.Data.IDbConnection mangos,
                                              System.Data.IDbConnection admin,
                                              ProfessionTuningStore.TuningRow row)
    {
        var orig = row.Orig;
        await mangos.ExecuteAsync(@"
            UPDATE spell_template SET
                reagentCount1 = @C1, reagentCount2 = @C2,
                reagentCount3 = @C3, reagentCount4 = @C4,
                reagentCount5 = @C5, reagentCount6 = @C6,
                reagentCount7 = @C7, reagentCount8 = @C8
            WHERE entry = @E AND build = @B",
            new
            {
                C1 = orig[0],
                C2 = orig[1],
                C3 = orig[2],
                C4 = orig[3],
                C5 = orig[4],
                C6 = orig[5],
                C7 = orig[6],
                C8 = orig[7],
                E = row.SpellEntry,
                B = row.Build
            });

        // Dropping the row is what makes the next patch-3 rebuild emit a
        // pristine Spell.dbc for this recipe.
        await ProfessionTuningStore.DeleteAsync(admin, row.SpellEntry);
    }

    // ===================== ROLLBACK ALL =====================

    [HttpPost]
    public async Task<IActionResult> RollbackAll()
    {
        try
        {
            using var mangos = _db.Mangos();
            using var admin = _db.Admin();

            var rows = await ProfessionTuningStore.GetAllAsync(admin);
            int restored = 0;
            foreach (var row in rows)
            {
                await RestoreOneAsync(mangos, admin, row);
                restored++;
            }

            await AuditAsync(
                "profession_tuning_rollback_all",
                "all professions",
                new { restored },
                reversible: false,
                $"Profession Tuning: rolled back {restored} recipe(s) to original reagent counts");

            _logger.LogInformation("ProfessionTuning: rolled back {N} recipes", restored);
            return Json(new { success = true, restored, needsClientRebuild = restored > 0 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProfessionTuning: RollbackAll failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ===================== STATUS =====================

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        using var admin = _db.Admin();
        var rows = await ProfessionTuningStore.GetAllAsync(admin);

        var tuned = rows.Select(r => new
        {
            spellEntry = r.SpellEntry,
            name = string.IsNullOrWhiteSpace(r.SpellName) ? $"Spell #{r.SpellEntry}" : r.SpellName,
            profession = TUNABLE_PROFESSIONS.FirstOrDefault(p => p.id == r.SkillLine).name
                         ?? $"Skill {r.SkillLine}",
            pct = r.Pct
        }).ToList();

        return Json(new { tuned });
    }

    // ===================== HELPERS =====================

    /// <summary>
    /// Audit a tuning mutation. These are direct spell_template writes, so they
    /// carry state and a reversibility flag like the lootifier's batch commits.
    /// StateBefore stays "{}" — the per-recipe originals live in
    /// vmangos_admin.profession_tuning, which is what Restore/Rollback replay
    /// from; duplicating every reagent count into the audit row would bloat it
    /// without adding a recovery path.
    /// </summary>
    private async Task AuditAsync(string action, string targetName, object stateAfter,
                                  bool reversible, string notes)
    {
        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = action,
            TargetType = "profession_tuning",
            TargetName = targetName,
            StateBefore = "{}",
            StateAfter = JsonSerializer.Serialize(stateAfter),
            IsReversible = reversible,
            Success = true,
            Notes = notes
        });
    }
}