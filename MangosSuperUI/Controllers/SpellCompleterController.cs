using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Dapper;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Spell Completer — the DATA phase of the two-app custom-spell workflow.
///
/// The DESIGN phase lives in MSUIClient's creator mode: spells are tuned there
/// and exported into a session file (spell-session.json) where each entry
/// carries the complete design — tuning metadata plus the patched M2 bytes and
/// recolored BLPs, base64-embedded. This page uploads that session, and for
/// each spell the user supplies what the design phase cannot know: the real
/// name, class/skill tab, damage, mana, levels, ranks. Completing a spell:
///
///   1. clones the source into spell_template (40000+ range) with the overrides,
///   2. wires skill_line_ability + spell_chain (and optional rank chain),
///   3. persists the design bytes via CompleterStore (per-path patched M2s and
///      verbatim files) under the spell's texture-cache directory,
///   4. saves the config row in custom_spell_meta so EVERY unified patch
///      rebuild reproduces the spell,
///
/// after which the client chains POST /Patch/RebuildClientPatch to produce
/// patch-3.MPQ with the full visual+data package.
/// </summary>
public class SpellCompleterController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly SpellCreatorService _spellCreator;
    private readonly SpellConfigService _spellConfig;
    private readonly DbcService _dbc;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<SpellCompleterController> _logger;

    private readonly RaService _ra;

    public SpellCompleterController(
        ConnectionFactory db,
        SpellCreatorService spellCreator,
        SpellConfigService spellConfig,
        DbcService dbc,
        IWebHostEnvironment env,
        IConfiguration config,
        RaService ra,
        ILogger<SpellCompleterController> logger)
    {
        _db = db;
        _spellCreator = spellCreator;
        _spellConfig = spellConfig;
        _dbc = dbc;
        _env = env;
        _config = config;
        _ra = ra;
        _logger = logger;
    }

    public IActionResult Index() => View();

    // ═══════════════════════════════════════════════════════════════════
    // SOURCE PREFILL — everything the inherited spell already knows
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The source spell's full gameplay profile, so the form starts
    /// from the inherited values instead of blank: damage, mana, levels, cast
    /// time, range, duration, cooldown, all three effect slots (type, aura,
    /// points, tick, misc), icon, description, rank count.</summary>
    [HttpGet]
    public async Task<IActionResult> SourceInfo(int entry)
    {
        if (entry <= 0) return Json(new { success = false, error = "bad entry" });
        try
        {
            using var conn = _db.Mangos();
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM spell_template WHERE entry = @E ORDER BY build DESC LIMIT 1",
                new { E = entry });
            if (row is null)
                return Json(new { success = false, error = $"spell_template has no #{entry}" });
            var d = (IDictionary<string, object>)row;

            long V(string col) => d.TryGetValue(col, out var v) && v != null ? Convert.ToInt64(v) : 0;
            string S(string col) => d.TryGetValue(col, out var v) ? v?.ToString() ?? "" : "";

            // Rank count of the source chain (how many ranks "Generate all ranks" makes)
            int rankCount = 1;
            try
            {
                var first = await conn.ExecuteScalarAsync<int?>(
                    "SELECT first_spell FROM spell_chain WHERE spell_id = @E LIMIT 1", new { E = entry });
                if (first is > 0)
                    rankCount = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM spell_chain WHERE first_spell = @F", new { F = first.Value });
            }
            catch { /* chain table empty for unranked spells */ }

            // Icon id: SQL first, client DBC as fallback (spell_template mirrors it)
            long iconId = V("spellIconId");
            if (iconId == 0 && _dbc.SpellEntries.TryGetValue((uint)entry, out var dbcRow))
                iconId = dbcRow.SpellIconId;

            var effects = new List<object>();
            for (int i = 1; i <= 3; i++)
                effects.Add(new
                {
                    slot = i,
                    effect = V($"effect{i}"),
                    aura = V($"effectApplyAuraName{i}"),
                    basePoints = V($"effectBasePoints{i}"),
                    dieSides = V($"effectDieSides{i}"),
                    amplitude = V($"effectAmplitude{i}"),
                    miscValue = V($"effectMiscValue{i}"),
                });

            return Json(new
            {
                success = true,
                entry,
                name = S("name"),
                nameSubtext = S("nameSubtext"),
                description = S("description"),
                school = V("school"),
                spellIconId = iconId,
                manaCost = V("manaCost"),
                spellLevel = V("spellLevel"),
                baseLevel = V("baseLevel"),
                maxLevel = V("maxLevel"),
                castingTimeIndex = V("castingTimeIndex"),
                rangeIndex = V("rangeIndex"),
                durationIndex = V("durationIndex"),
                speed = d.TryGetValue("speed", out var sp) && sp != null ? Convert.ToSingle(sp) : 0f,
                cooldown = V("recoveryTime"),
                rankCount,
                effects,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completer: SourceInfo({Entry}) failed", entry);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TRAINER DIAGNOSTIC — the whole chain a rank needs to reach a trainer
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Why is (or isn't) this custom spell at trainers? For each rank:
    /// its learn-spell wrapper (50000+, effect 36) and every npc_trainer /
    /// npc_trainer_template row pointing at that wrapper. If rows exist here
    /// but nothing shows in game, the world server hasn't been restarted —
    /// these tables load once at mangosd startup.</summary>
    [HttpGet]
    public async Task<IActionResult> TrainerStatus(int entry)
    {
        try
        {
            using var conn = _db.Mangos();
            var ranks = (await conn.QueryAsync<(int rank, int spell_id)>(
                @"SELECT rank, spell_id FROM spell_chain WHERE first_spell = @E ORDER BY rank",
                new { E = entry })).ToList();
            if (ranks.Count == 0) ranks.Add((1, entry));

            var report = new List<object>();
            var allWrappers = new List<int>();
            foreach (var (rank, spellId) in ranks)
            {
                var wrappers = (await conn.QueryAsync<int>(
                    @"SELECT entry FROM spell_template WHERE effect1 = 36 AND effectTriggerSpell1 = @S",
                    new { S = spellId })).ToList();
                allWrappers.AddRange(wrappers);
                var search = new List<int>(wrappers) { spellId };
                var direct = (await conn.QueryAsync<(int entry, int spell, int reqlevel)>(
                    @"SELECT entry, spell, reqlevel FROM npc_trainer WHERE spell IN @S",
                    new { S = search })).ToList();
                var template = (await conn.QueryAsync<(int entry, int spell, int reqlevel)>(
                    @"SELECT entry, spell, reqlevel FROM npc_trainer_template WHERE spell IN @S",
                    new { S = search })).ToList();
                report.Add(new
                {
                    rank,
                    spellId,
                    wrappers,
                    npcTrainerRows = direct.Select(t => new { trainerNpc = t.entry, t.spell, t.reqlevel }),
                    trainerTemplateRows = template.Select(t => new { templateId = t.entry, t.spell, t.reqlevel }),
                    atTrainers = direct.Count + template.Count > 0,
                });
            }
            // Ground truth, not inference: open the BUILT patch-3.MPQ and check
            // whether each wrapper actually has a Spell.dbc row in it. The client
            // silently hides trainer entries whose wrapper spell is missing from
            // its Spell.dbc, so this is the decisive client-side check.
            string? patchBuiltAtUtc = null;
            var wrappersInPatch = new Dictionary<uint, bool>();
            string patchFile = Path.Combine(
                _config["Vmangos:PatchOutputPath"] ?? Path.Combine(_env.WebRootPath, "patches"),
                "patch-3.MPQ");
            if (!System.IO.File.Exists(patchFile))
                patchFile = Path.Combine(_env.WebRootPath, "patches", "patch-3.MPQ");
            if (System.IO.File.Exists(patchFile))
            {
                patchBuiltAtUtc = System.IO.File.GetLastWriteTimeUtc(patchFile).ToString("o");
                try
                {
                    using var archive = Services.Mpq.MpqArchive.Open(patchFile);
                    byte[]? spellDbcBytes = archive?.ReadFile(@"DBFilesClient\Spell.dbc");
                    if (spellDbcBytes is not null)
                    {
                        var spellDbc = DbcWriterService.ReadDbc(spellDbcBytes, "patch-3 Spell.dbc");
                        foreach (int w in allWrappers)
                            wrappersInPatch[(uint)w] = spellDbc.GetRow((uint)w) != null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: could not inspect {Patch}", patchFile);
                }
            }

            return Json(new
            {
                success = true,
                entry,
                ranks = report,
                patchBuiltAtUtc,
                patchFileChecked = patchFile,
                // wrapper spell id -> is its Spell.dbc row inside the built patch?
                wrappersInBuiltPatch = wrappersInPatch.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                note = "atTrainers=true + wrappersInBuiltPatch=true for every rank means BOTH sides are " +
                       "correct here — remaining causes are deployment order: the patch actually installed " +
                       "in WoW/Data is an older copy, or mangosd was last restarted before these " +
                       "wrapper/trainer rows were created. wrappersInBuiltPatch=false means the rebuild " +
                       "predates the wrappers — rebuild again and reinstall. No wrapper/rows at all = " +
                       "complete the spell again with 'Copy source trainers' checked.",
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Interrogate the RUNNING world server: does its in-memory data
    /// include this spell (i.e. was mangosd restarted after creation), and do
    /// its log files complain about the trainer rows? Complements TrainerStatus
    /// (SQL + patch file), closing the last unverifiable link in the chain.</summary>
    [HttpGet]
    public async Task<IActionResult> ServerStatus(string name, int? wrapperFrom = null, int? wrapperTo = null)
    {
        string? lookupResult = null;
        string? lookupError = null;
        try
        {
            // "lookup spell X" answers from mangosd's LIVE spell store, not SQL.
            lookupResult = await _ra.SendCommandAsync($"lookup spell {name}");
        }
        catch (Exception ex)
        {
            lookupError = ex.Message;
        }

        // Grep the server logs for trainer-load complaints and wrapper mentions.
        var logHits = new List<string>();
        string? logsDir = _config["Vmangos:LogsDir"];
        if (!string.IsNullOrEmpty(logsDir) && Directory.Exists(logsDir))
        {
            int from = wrapperFrom ?? 50000, to = wrapperTo ?? 65000;
            foreach (string file in Directory.GetFiles(logsDir, "*.log")
                         .OrderByDescending(System.IO.File.GetLastWriteTimeUtc).Take(3))
            {
                try
                {
                    foreach (string line in System.IO.File.ReadLines(file))
                    {
                        bool trainerComplaint = line.Contains("npc_trainer", StringComparison.OrdinalIgnoreCase);
                        bool wrapperMention = false;
                        if (!trainerComplaint)
                            for (int w = from; w <= to && !wrapperMention; w += 1)
                            {
                                // cheap contains for each id would be slow over a range;
                                // only scan for ids we can cheaply detect
                                if (to - from > 50) break;
                                wrapperMention = line.Contains(w.ToString());
                            }
                        if (trainerComplaint || wrapperMention)
                        {
                            logHits.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                            if (logHits.Count >= 80) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logHits.Add($"{Path.GetFileName(file)}: <unreadable: {ex.Message}>");
                }
                if (logHits.Count >= 80) break;
            }
        }
        else
        {
            logHits.Add($"<logs dir not found: '{logsDir}'>");
        }

        return Json(new
        {
            success = true,
            raConnected = _ra.IsConnected,
            lookup = lookupResult,
            lookupError,
            note = "If 'lookup' does NOT list the custom spell, the running mangosd predates it — " +
                   "restart the world server. If it lists it but trainers still lack it, read logHits " +
                   "for npc_trainer load complaints.",
            logHits,
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // REFERENCE LISTS — labeled dropdowns instead of raw DBC indexes
    // ═══════════════════════════════════════════════════════════════════

    private static object? _refsCache;

    /// <summary>Duration / cast-time / range indexes with human labels, read
    /// straight from the server-side DBC directory. Cached per process.</summary>
    [HttpGet]
    public IActionResult Refs()
    {
        if (_refsCache is not null) return Json(_refsCache);
        string dbcPath = _config["Vmangos:DbcPath"] ?? "/home/wowvmangos/vmangos/run/data/5875/dbc";

        List<object> ReadRef(string file, Func<uint[], string> label)
        {
            var result = new List<object>();
            try
            {
                string path = Path.Combine(dbcPath, file);
                if (!System.IO.File.Exists(path)) return result;
                foreach (var row in DbcWriterService.ReadDbc(path).GetAllRows())
                    result.Add(new { id = row[0], label = label(row) });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Completer: failed reading {File}", file);
            }
            return result;
        }

        string Ms(long ms) => ms < 0 ? "until cancelled"
            : ms == 0 ? "instant"
            : ms % 60000 == 0 && ms >= 60000 ? $"{ms / 60000} min"
            : $"{ms / 1000.0:0.#} s";

        _refsCache = new
        {
            // SpellDuration.dbc: [1] = base duration ms (int; -1 = infinite)
            durations = ReadRef("SpellDuration.dbc", r => Ms(unchecked((int)r[1]))),
            // SpellCastTimes.dbc: [1] = base cast ms
            castTimes = ReadRef("SpellCastTimes.dbc", r => Ms(unchecked((int)r[1]))),
            // SpellRange.dbc: [1]/[2] = min/max yards (floats)
            ranges = ReadRef("SpellRange.dbc", r =>
            {
                float min = DbcWriterService.UintToFloat(r[1]);
                float max = DbcWriterService.UintToFloat(r[2]);
                return min > 0 ? $"{min:0}–{max:0} yd" : max > 0 ? $"{max:0} yd" : "self";
            }),
        };
        return Json(_refsCache);
    }

    /// <summary>Complete one spell from an uploaded MSUIClient session: create the
    /// SQL rows, persist the design bytes, save the rebuild config.</summary>
    [HttpPost]
    public async Task<IActionResult> Complete([FromBody] CompleteSpellRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SpellName))
            return Json(new { success = false, error = "Spell name is required." });
        if (CompleterStore.SafeName(req.SpellName).Length == 0)
            return Json(new { success = false, error = "Spell name needs letters or digits." });
        if (req.SourceSpellEntry <= 0)
            return Json(new { success = false, error = "Session entry has no source spell." });

        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

        try
        {
            // ── 1: spell_template clone with the user's data-phase values ──
            // (mirrors PatchController.Generate's override construction)
            var overrides = new Dictionary<string, object?>
            {
                ["name"] = req.SpellName,
                ["nameSubtext"] = req.NameSubtext ?? "Rank 1",
                ["school"] = req.School,
            };
            if (!string.IsNullOrWhiteSpace(req.Description))
                overrides["description"] = req.Description;
            if (req.DamageMin.HasValue && req.DamageMax.HasValue)
            {
                overrides["effectBasePoints1"] = req.DamageMin.Value - 1;            // min = base + 1
                overrides["effectDieSides1"] = req.DamageMax.Value - req.DamageMin.Value;
            }
            if (req.ManaCost.HasValue) overrides["manaCost"] = req.ManaCost.Value;
            if (req.SpellLevel.HasValue)
            {
                overrides["spellLevel"] = req.SpellLevel.Value;
                overrides["baseLevel"] = req.SpellLevel.Value;
            }
            if (req.MaxLevel.HasValue) overrides["maxLevel"] = req.MaxLevel.Value;
            if (req.CastingTimeIndex.HasValue) overrides["castingTimeIndex"] = req.CastingTimeIndex.Value;
            if (req.RangeIndex.HasValue) overrides["rangeIndex"] = req.RangeIndex.Value;
            if (req.DurationIndex.HasValue) overrides["durationIndex"] = req.DurationIndex.Value;
            if (req.Cooldown.HasValue) overrides["recoveryTime"] = req.Cooldown.Value;

            // Effect-slot overrides: the mechanics editor (School Damage / DoT /
            // Slow / Heal ...). Only CHANGED slots arrive; each carries its full
            // value set. Points use the house convention: base = min-1,
            // dieSides = max-min (mirrored to DBC with dieSides+1).
            // These are also remembered for the rank chain below — the generator
            // clones ranks 2+ from the SOURCE ranks, which still have the source
            // mechanics, so the structure must ride the per-rank overrides too.
            var effectStructure = new Dictionary<string, object?>();
            foreach (var ef in req.Effects ?? new List<CompleterEffectDto>())
            {
                if (ef.Slot is < 1 or > 3) continue;
                effectStructure[$"effect{ef.Slot}"] = ef.Effect;
                effectStructure[$"effectApplyAuraName{ef.Slot}"] = ef.Effect == 6 ? ef.Aura : 0;
                if (ef.PointsMin.HasValue && ef.PointsMax.HasValue)
                {
                    effectStructure[$"effectBasePoints{ef.Slot}"] = ef.PointsMin.Value - 1;
                    effectStructure[$"effectDieSides{ef.Slot}"] = ef.PointsMax.Value - ef.PointsMin.Value;
                }
                if (ef.Amplitude.HasValue) effectStructure[$"effectAmplitude{ef.Slot}"] = ef.Amplitude.Value;
                if (ef.MiscValue.HasValue) effectStructure[$"effectMiscValue{ef.Slot}"] = ef.MiscValue.Value;
            }
            foreach (var (col, value) in effectStructure) overrides[col] = value;
            if (!string.IsNullOrEmpty(req.SkillTabKey))
            {
                var tabMap = SpellCreatorService.GetSkillTabMap();
                if (tabMap.TryGetValue(req.SkillTabKey, out var tabInfo))
                    overrides["spellFamilyName"] = tabInfo.spellFamilyName;
            }

            int newEntry = await _spellCreator.CloneSpellAsync(req.SourceSpellEntry, overrides, ip);
            if (newEntry < 0)
                return Json(new { success = false, error = "Failed to create spell in database." });

            // ── 2: spellbook tab + rank chain root ──
            if (!string.IsNullOrEmpty(req.SkillTabKey))
            {
                try
                {
                    await _spellCreator.InsertSkillLineAbilityAsync(newEntry, req.SkillTabKey, learnOnGetSkill: 2);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: skill_line_ability insert failed for #{Entry}", newEntry);
                }
            }
            else
            {
                // No tab picked: prefer the tab MATCHING THE CHOSEN SCHOOL within
                // the source spell's class (School=Fire on a mage spell → Mage—Fire),
                // falling back to the source spell's own tab. School (damage type)
                // and skill line (trainer/spellbook category) are independent
                // fields in WoW's data — but when the user picks a school, the
                // matching category is almost always what they mean.
                string? schoolTabKey = null;
                try
                {
                    using var probeConn = _spellCreator.CreateMangosConnection();
                    var probeSla = await probeConn.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT class_mask FROM skill_line_ability
                          WHERE spell_id = @E AND build = 5875 LIMIT 1",
                        new { E = req.SourceSpellEntry });
                    int sourceClassMask = probeSla != null ? (int)(probeSla.class_mask ?? 0) : 0;
                    string[] schoolNames = { "physical", "holy", "fire", "nature", "frost", "shadow", "arcane" };
                    if (sourceClassMask > 0 && req.School >= 0 && req.School < schoolNames.Length)
                    {
                        string suffix = "_" + schoolNames[req.School];
                        schoolTabKey = SpellCreatorService.GetSkillTabMap()
                            .FirstOrDefault(t => t.Value.classMask == sourceClassMask &&
                                                 t.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).Key;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: school-tab match probe failed for #{Entry}", newEntry);
                }

                if (schoolTabKey is not null)
                {
                    try
                    {
                        await _spellCreator.InsertSkillLineAbilityAsync(newEntry, schoolTabKey, learnOnGetSkill: 2);
                        _logger.LogInformation("Completer: #{Entry} auto-tabbed to '{Tab}' (matches chosen school)",
                            newEntry, schoolTabKey);
                        req.SkillTabKey = schoolTabKey;   // rank chain + family name follow the same tab
                        var tabMap2 = SpellCreatorService.GetSkillTabMap();
                        if (tabMap2.TryGetValue(schoolTabKey, out var tabInfo2))
                        {
                            overrides["spellFamilyName"] = tabInfo2.spellFamilyName;
                            using var famConn = _spellCreator.CreateMangosConnection();
                            await famConn.ExecuteAsync(
                                "UPDATE spell_template SET spellFamilyName = @F WHERE entry = @E AND build = 5875",
                                new { F = tabInfo2.spellFamilyName, E = newEntry });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Completer: school-tab insert failed for #{Entry}", newEntry);
                        schoolTabKey = null;   // fall through to source inherit
                    }
                }

                if (schoolTabKey is null)
                {
                //
                // The insert MUST allocate `id`: skill_line_ability's primary key
                // has no default and no auto-increment, so an id-less INSERT
                // IGNORE silently no-ops. The rebuild then reads skill 0/class 0
                // and writes a dead SkillLineAbility.dbc row — which the client
                // cannot categorize, so the spell VANISHES from trainer lists
                // even though the server offers it. (This is exactly how
                // FireFunnel went missing at the mage trainers.)
                try
                {
                    using var slaConn = _spellCreator.CreateMangosConnection();
                    var sourceSla = await slaConn.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT skill_id, class_mask FROM skill_line_ability
                          WHERE spell_id = @E AND build = 5875 LIMIT 1",
                        new { E = req.SourceSpellEntry });
                    if (sourceSla != null && (int)(sourceSla.skill_id ?? 0) > 0)
                    {
                        int nextId = await slaConn.ExecuteScalarAsync<int>(
                            "SELECT COALESCE(MAX(id), 0) + 1 FROM skill_line_ability");
                        int affected = await slaConn.ExecuteAsync(
                            @"INSERT IGNORE INTO skill_line_ability
                              (id, build, skill_id, spell_id, race_mask, class_mask, req_skill_value,
                               superseded_by_spell, learn_on_get_skill, max_value, min_value, req_train_points)
                              VALUES (@Id, 5875, @SkillId, @SpellId, 0, @ClassMask, 1, 0, 2, 0, 0, 0)",
                            new
                            {
                                Id = nextId,
                                SkillId = (int)sourceSla.skill_id,
                                SpellId = newEntry,
                                ClassMask = (int)(sourceSla.class_mask ?? 0),
                            });
                        if (affected == 0)
                            _logger.LogWarning("Completer: SLA inherit insert ignored for #{Entry} (id {Id})",
                                newEntry, nextId);
                    }
                    else
                    {
                        _logger.LogWarning("Completer: source #{Src} has no skill_line_ability row to " +
                            "inherit — #{Entry} will rely on the rebuild's source fallback", req.SourceSpellEntry, newEntry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: SLA auto-copy failed for #{Entry}", newEntry);
                }
                }
            }

            try
            {
                await _spellCreator.InsertSpellChainAsync(newEntry, prevSpell: 0, firstSpell: newEntry, rank: 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Completer: spell_chain insert failed for #{Entry}", newEntry);
            }

            // ── 2b: register Rank 1 at trainers ──
            // (The rank generator only registers ranks 2+; without this step the
            // first rank never appears at any trainer.) Mirrors Generate's flow:
            // copy the source spell's trainer locations via a learn-spell wrapper;
            // if the source is a starting spell with no trainer entries, fall
            // back to registering at all class trainers for the chosen tab.
            if (req.CopySourceTrainers)
            {
                try
                {
                    int trainersCopied = await _spellCreator.CopyTrainerEntriesFromSourceAsync(
                        req.SourceSpellEntry, newEntry, 0, req.SpellLevel ?? 1);
                    if (trainersCopied > 0)
                    {
                        _logger.LogInformation("Completer: Copied {Count} trainer entries from source #{Src} to R1 #{New}",
                            trainersCopied, req.SourceSpellEntry, newEntry);
                    }
                    else if (!string.IsNullOrEmpty(req.SkillTabKey))
                    {
                        var tabMap = SpellCreatorService.GetSkillTabMap();
                        if (tabMap.TryGetValue(req.SkillTabKey, out var tabInfo))
                        {
                            // classMask = 1 << (classId - 1) → recover the classId
                            int classId = 0;
                            int mask = tabInfo.classMask;
                            while (mask > 1) { mask >>= 1; classId++; }
                            classId++;

                            var templateMap = SpellCreatorService.GetClassTrainerTemplateMap();
                            if (templateMap.TryGetValue(classId, out int templateId))
                            {
                                int iconId = 185;
                                try
                                {
                                    using var iconConn = _spellCreator.CreateMangosConnection();
                                    var icon = await Dapper.SqlMapper.ExecuteScalarAsync<int?>(iconConn,
                                        "SELECT spellIconId FROM spell_template WHERE entry = @E AND build = 5875",
                                        new { E = newEntry });
                                    iconId = icon ?? 185;
                                }
                                catch { /* fallback icon */ }

                                int wrapperId = await _spellCreator.CreateTrainerWrapperAsync(
                                    newEntry, req.SpellName, req.NameSubtext ?? "Rank 1",
                                    req.SpellLevel ?? 1, iconId);
                                await _spellCreator.InsertNpcTrainerTemplateAsync(
                                    templateId, wrapperId, 100, req.SpellLevel ?? 1);
                                _logger.LogInformation(
                                    "Completer: Registered R1 #{New} at class trainer template {Tmpl} via wrapper #{Wrap}",
                                    newEntry, templateId, wrapperId);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Completer: source #{Src} has no trainer entries and no " +
                            "skill tab was chosen — R1 #{New} is not at any trainer", req.SourceSpellEntry, newEntry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: trainer registration failed for R1 #{Entry}", newEntry);
                }
            }

            // ── 3: optional full rank chain, scaled per-rank like the Creator page ──
            List<(int rank, int entry)>? ranks = null;
            if (req.GenerateAllRanks)
            {
                try
                {
                    Dictionary<int, Dictionary<string, object?>>? perRank = null;

                    // Carry the mechanics changes to EVERY rank: ranks 2+ are
                    // cloned from the source ranks (which keep source mechanics),
                    // so without this a DoT'd rank 1 gets vanilla ranks 2+.
                    // Injected for a generous rank range; only existing ranks match.
                    if (effectStructure.Count > 0 || req.DurationIndex.HasValue)
                    {
                        perRank = new Dictionary<int, Dictionary<string, object?>>();
                        for (int rank = 2; rank <= 25; rank++)
                        {
                            var d = new Dictionary<string, object?>(effectStructure);
                            if (req.DurationIndex.HasValue) d["durationIndex"] = req.DurationIndex.Value;
                            perRank[rank] = d;
                        }
                    }

                    if (req.RankOverrides is { Count: > 0 })
                    {
                        perRank ??= new Dictionary<int, Dictionary<string, object?>>();
                        foreach (var (rank, ro) in req.RankOverrides)
                        {
                            // Merge ON TOP of the injected structure dict (never
                            // replace it — that would strip the mechanics).
                            var d = perRank.TryGetValue(rank, out var existing)
                                ? existing : new Dictionary<string, object?>();
                            if (ro.DamageMin.HasValue && ro.DamageMax.HasValue)
                            {
                                d["effectBasePoints1"] = ro.DamageMin.Value - 1;
                                d["effectDieSides1"] = ro.DamageMax.Value - ro.DamageMin.Value;
                            }
                            if (ro.ManaCost.HasValue) d["manaCost"] = ro.ManaCost.Value;
                            if (ro.SpellLevel.HasValue)
                            {
                                d["spellLevel"] = ro.SpellLevel.Value;
                                d["baseLevel"] = ro.SpellLevel.Value;
                            }
                            if (d.Count > 0) perRank[rank] = d;
                        }
                    }

                    ranks = await _spellCreator.GenerateRankChainAsync(
                        existingRank1Entry: newEntry,
                        sourceFirstSpell: req.SourceSpellEntry,
                        spellName: req.SpellName,
                        description: req.Description,
                        school: req.School,
                        skillTabKey: req.SkillTabKey,
                        rank1Overrides: overrides,
                        perRankOverrides: perRank,
                        operatorIp: ip,
                        copySourceTrainers: req.CopySourceTrainers);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Completer: rank chain generation failed for #{Entry}", newEntry);
                }
            }

            // ── 4: persist the design bytes for every future patch rebuild ──
            var pathM2s = new List<(string originalPath, byte[] bytes)>();
            var extraFiles = new List<(string mpqPath, byte[] bytes)>();
            foreach (var model in req.Models ?? new List<CompleterModelDto>())
            {
                if (string.IsNullOrEmpty(model.M2Base64) || string.IsNullOrEmpty(model.Path)) continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(model.M2Base64); }
                catch { return Json(new { success = false, error = $"Bad m2Base64 for {model.Path}" }); }

                // Geometry models (per-particle M2s) are referenced from INSIDE the
                // host model's bytes — their path cannot be re-pointed at a clone,
                // so they ship verbatim at their original path (global override).
                if (model.Phases?.StartsWith("geometry", StringComparison.OrdinalIgnoreCase) == true)
                    extraFiles.Add((SpellVisualCloner.NormalizeM2Extension(model.Path), bytes));
                else
                    pathM2s.Add((model.Path, bytes));
            }
            foreach (var blp in req.TintedBlps ?? new List<CompleterBlpDto>())
            {
                if (string.IsNullOrEmpty(blp.BlpBase64) || string.IsNullOrEmpty(blp.Path)) continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(blp.BlpBase64); }
                catch { return Json(new { success = false, error = $"Bad blpBase64 for {blp.Path}" }); }
                extraFiles.Add((blp.Path, bytes));
            }

            CompleterStore.Save(_env.WebRootPath, req.SpellName,
                new CompleterStore.Manifest
                {
                    TempName = req.TempName ?? "",
                    SourceSpellEntry = req.SourceSpellEntry,
                    ExportedAtUtc = req.ExportedAtUtc ?? "",
                },
                pathM2s, extraFiles);

            // ── 5: the config row that makes rebuilds reproduce this spell ──
            // Icon: "custom" reuses one of the user's generated PNGs (the same
            // embed pipeline as the Creator page — IconSource "comfyui-flux");
            // "source" keeps the inherited spell's vanilla icon (resolved at
            // rebuild); anything else falls back to the school icon.
            string iconSource = "school";
            string? iconPath = null;
            if (req.IconSource == "custom" && !string.IsNullOrEmpty(req.IconPath))
            {
                string customIconDir = Path.GetFullPath(
                    Path.Combine(_env.WebRootPath, "images", "icons", "custom"));
                string resolved = Path.GetFullPath(req.IconPath);
                if (resolved.StartsWith(customIconDir, StringComparison.OrdinalIgnoreCase) &&
                    System.IO.File.Exists(resolved))
                {
                    iconSource = "comfyui-flux";
                    iconPath = resolved;
                }
                else
                {
                    _logger.LogWarning("Completer: rejected icon path outside custom dir: {Path}", req.IconPath);
                }
            }
            else if (req.IconSource == "source")
            {
                iconSource = "source";
            }

            await _spellConfig.SaveConfigAsync(new SpellVisualConfig
            {
                Entry = newEntry,
                SourceEntry = req.SourceSpellEntry,
                SpellName = req.SpellName,
                NameSubtext = req.NameSubtext ?? "Rank 1",
                Description = req.Description,
                IconSource = iconSource,
                IconPath = iconPath,
            });

            _logger.LogInformation(
                "Completer: '{Temp}' -> #{Entry} {Name} ({M2s} per-path M2(s), {Extra} extra file(s), {Ranks} rank(s))",
                req.TempName, newEntry, req.SpellName, pathM2s.Count, extraFiles.Count, ranks?.Count ?? 1);

            return Json(new
            {
                success = true,
                spellEntry = newEntry,
                ranksGenerated = ranks?.Count ?? 0,
                m2Count = pathM2s.Count,
                extraFileCount = extraFiles.Count,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completer: failed to complete spell '{Name}'", req.SpellName);
            return Json(new { success = false, error = ex.Message });
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// REQUEST DTOs — the JS posts the finalization form plus the relevant slice
// of the uploaded session (models with bytes, tinted BLPs).
// ═══════════════════════════════════════════════════════════════════════

public class CompleteSpellRequest
{
    public string? TempName { get; set; }
    public int SourceSpellEntry { get; set; }
    public string? ExportedAtUtc { get; set; }

    public string SpellName { get; set; } = "";
    public string? NameSubtext { get; set; }
    public string? Description { get; set; }
    public int School { get; set; }
    public string? SkillTabKey { get; set; }
    public int? DamageMin { get; set; }
    public int? DamageMax { get; set; }
    public int? ManaCost { get; set; }
    public int? SpellLevel { get; set; }
    public int? MaxLevel { get; set; }
    public int? CastingTimeIndex { get; set; }
    public int? RangeIndex { get; set; }
    public int? DurationIndex { get; set; }
    public int? Cooldown { get; set; }
    public bool CopySourceTrainers { get; set; }
    public bool GenerateAllRanks { get; set; }
    public Dictionary<int, CompleterRankOverride>? RankOverrides { get; set; }

    /// <summary>"source" (inherit the source spell's icon), "custom" (IconPath
    /// names a PNG under wwwroot/images/icons/custom), or "school".</summary>
    public string? IconSource { get; set; }
    public string? IconPath { get; set; }

    /// <summary>Changed effect slots from the mechanics editor. Each carries its
    /// complete value set (type, aura, points, tick, misc).</summary>
    public List<CompleterEffectDto>? Effects { get; set; }

    public List<CompleterModelDto>? Models { get; set; }
    public List<CompleterBlpDto>? TintedBlps { get; set; }
}

public class CompleterEffectDto
{
    /// <summary>1-based effect slot (spell_template effect1..3).</summary>
    public int Slot { get; set; }
    /// <summary>SPELL_EFFECT_* id (0 none, 2 school damage, 6 apply aura, 10 heal, 30 energize, 31 weapon % dmg).</summary>
    public int Effect { get; set; }
    /// <summary>SPELL_AURA_* id when Effect is 6 (3 periodic damage, 8 periodic heal, 33 decrease speed, ...).</summary>
    public int Aura { get; set; }
    public int? PointsMin { get; set; }
    public int? PointsMax { get; set; }
    /// <summary>Tick interval in ms for periodic auras.</summary>
    public int? Amplitude { get; set; }
    public int? MiscValue { get; set; }
}

public class CompleterRankOverride
{
    public int? DamageMin { get; set; }
    public int? DamageMax { get; set; }
    public int? ManaCost { get; set; }
    public int? SpellLevel { get; set; }
}

public class CompleterModelDto
{
    public string Path { get; set; } = "";
    public string? Phases { get; set; }
    public string? M2Base64 { get; set; }
}

public class CompleterBlpDto
{
    public string Path { get; set; } = "";
    public string? BlpBase64 { get; set; }
}
