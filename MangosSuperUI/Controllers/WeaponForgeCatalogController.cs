using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using MangosSuperUI.Services.SpellServices;
using MangosSuperUI.Services.WeaponForge;
using Microsoft.AspNetCore.Mvc;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Read-only catalogs used while configuring a forged item. The spell-effect list is grounded in
/// two pieces of Vanilla truth: the active 1.12 Spell.dbc and the immutable original-item
/// baseline. It never invents rating-to-percent conversions or creates new spells.
/// </summary>
[Route("WeaponForge")]
public sealed class WeaponForgeCatalogController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly VanillaItemSpellCatalog _itemSpells;
    private readonly ILogger<WeaponForgeCatalogController> _logger;

    public WeaponForgeCatalogController(ConnectionFactory db, DbcService dbc,
        VanillaItemSpellCatalog itemSpells, ILogger<WeaponForgeCatalogController> logger)
    {
        _db = db;
        _dbc = dbc;
        _itemSpells = itemSpells;
        _logger = logger;
    }

    /// <summary>
    /// Search real item-use/equip/proc spells from the installed Vanilla client data.
    /// Both text and exact-ID searches return only spell/trigger pairs used by stock items. This
    /// preserves native item semantics: an ordinary cast such as Fireball cannot be relabeled as
    /// an On Equip effect merely because its spell ID exists.
    /// </summary>
    [HttpGet("ItemSpellEffects")]
    public async Task<IActionResult> ItemSpellEffects(string? q = null, int? trigger = null, int limit = 40)
    {
        if (trigger is < 0 or > 2)
            return BadRequest(new { ok = false, error = "Trigger must be 0 (Use), 1 (On Equip), or 2 (Chance on Hit)." });

        if (!_dbc.IsLoaded || _dbc.AllSpellEntries.Count == 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                error = "The active Vanilla Spell.dbc catalog is unavailable; item effects cannot be validated."
            });

        limit = Math.Clamp(limit, 1, 80);
        string query = (q ?? string.Empty).Trim();
        bool exactId = uint.TryParse(query, out uint requestedId);

        IReadOnlyList<NativeItemSpellUsage> usage;
        try
        {
            usage = await _itemSpells.GetUsageAsync(HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WeaponForge: immutable Vanilla item-effect baseline is unavailable");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                error = "The original Vanilla item baseline is unavailable; native item effects cannot be verified."
            });
        }

        IEnumerable<NativeItemSpellUsage> candidates = usage;
        if (trigger.HasValue)
            candidates = candidates.Where(x => x.TriggerValue == trigger.Value);
        if (exactId)
            candidates = candidates.Where(x => x.SpellId == requestedId);

        var results = new List<ItemSpellEffectResult>();
        foreach (var row in candidates)
        {
            if (!_dbc.AllSpellEntries.TryGetValue(row.SpellId, out var spell))
                continue;
            if (!exactId && query.Length > 0 && !Matches(spell, query))
                continue;

            results.Add(ToResult(spell, row, true));
        }

        results = results
            .OrderByDescending(x => x.UsedByCount)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToList();

        try
        {
            results = await FormatDescriptionsAsync(results, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WeaponForge: optional native item-effect tooltip formatting failed");
        }

        return Json(new
        {
            ok = true,
            results,
            installedSpellCount = _dbc.AllSpellEntries.Count,
            note = "Flat item stats stay flat; percentage and other passive bonuses are listed only as their native Vanilla item spells."
        });
    }

    private ItemSpellEffectResult ToResult(SpellDbcEntry spell, NativeItemSpellUsage usage, bool stockItemEffect) =>
        new(
            spell.Entry,
            string.IsNullOrWhiteSpace(spell.Name) ? $"Spell #{spell.Entry}" : spell.Name,
            spell.NameSubtext,
            spell.Description,
            usage.TriggerValue,
            TriggerLabel(usage.TriggerValue),
            usage.Charges,
            usage.PpmRate,
            usage.CooldownMs,
            usage.Category,
            usage.CategoryCooldownMs,
            usage.UsedByCount,
            stockItemEffect,
            spell.Hidden,
            _dbc.GetSpellIconPath(spell.SpellIconId));

    private async Task<List<ItemSpellEffectResult>> FormatDescriptionsAsync(
        List<ItemSpellEffectResult> results, CancellationToken cancellationToken)
    {
        if (results.Count == 0) return results;

        using var conn = _db.Admin();
        var command = new CommandDefinition(
            """
            SELECT spell.* FROM og_spell_template spell
             WHERE spell.entry IN @Ids
               AND spell.build = (SELECT MAX(s2.build) FROM og_spell_template s2 WHERE s2.entry = spell.entry)
            """,
            new { Ids = results.Select(x => x.Id).ToArray() }, cancellationToken: cancellationToken);
        var rows = await conn.QueryAsync<dynamic>(command);

        var byId = new Dictionary<uint, IDictionary<string, object>>();
        foreach (var row in rows)
        {
            var values = (IDictionary<string, object>)row;
            uint id = Convert.ToUInt32(values["entry"]);
            byId[id] = values;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (string.IsNullOrWhiteSpace(result.Description) || !byId.TryGetValue(result.Id, out var row))
                continue;
            results[i] = result with
            {
                Description = SpellTooltipFormatter.Format(result.Description, BuildSpellNumbers(result.Id, row))
            };
        }

        return results;
    }

    private SpellTooltipFormatter.SpellNumbers BuildSpellNumbers(
        uint entry, IDictionary<string, object> row)
    {
        int GetInt(string column)
        {
            if (!row.TryGetValue(column, out var value) || value is null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        var numbers = new SpellTooltipFormatter.SpellNumbers
        {
            Entry = entry,
            ProcChance = GetInt("procChance"),
            ProcCharges = GetInt("procCharges"),
            StackAmount = GetInt("stackAmount"),
            MaxAffectedTargets = GetInt("maxAffectedTargets")
        };

        for (int i = 0; i < 3; i++)
        {
            int slot = i + 1;
            numbers.BasePoints[i] = GetInt($"effectBasePoints{slot}");
            numbers.DieSides[i] = GetInt($"effectDieSides{slot}");
            numbers.BaseDice[i] = GetInt($"effectBaseDice{slot}");
            numbers.Amplitude[i] = GetInt($"effectAmplitude{slot}");
            numbers.ChainTargets[i] = GetInt($"effectChainTarget{slot}");
        }

        uint durationIndex = (uint)Math.Max(0, GetInt("durationIndex"));
        if (_dbc.SpellDurations.TryGetValue(durationIndex, out var duration))
            numbers.DurationMs = duration.DurationMs;

        return numbers;
    }

    private static bool Matches(SpellDbcEntry spell, string query) =>
        spell.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        spell.NameSubtext.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        spell.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        spell.Entry.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string TriggerLabel(int trigger) => trigger switch
    {
        0 => "Use",
        1 => "On Equip",
        2 => "Chance on Hit",
        _ => "Unknown"
    };

    private sealed record ItemSpellEffectResult(
        uint Id,
        string Name,
        string Subtext,
        string Description,
        int Trigger,
        string TriggerLabel,
        int Charges,
        float PpmRate,
        int CooldownMs,
        int Category,
        int CategoryCooldownMs,
        int UsedByCount,
        bool StockItemEffect,
        bool Hidden,
        string IconPath);
}
