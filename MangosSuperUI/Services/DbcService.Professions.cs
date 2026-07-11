using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangosSuperUI.Services;

// ══════════════════════════════════════════════════════════════════════════
//  DbcService — profession recipe enumeration (partial)
//
//  Maps each gear-making profession to its craftable OUTPUT items, so the
//  Crafting Lootifier can browse recipes and batch-lootify a whole profession.
//
//  Source of truth is DBC (there's no skill_line_ability in SQL on VMaNGOS):
//    * SkillLineAbility.dbc  field[1]=skillLine  field[2]=spell  field[7]=minRank
//    * Spell.dbc effect 24 (SPELL_EFFECT_CREATE_ITEM) → EffectItemType = output
//        Offsets anchored to the confirmed 1.12.1 layout (name@120):
//        Effect[0..2] @ 61-63, EffectItemType[0..2] @ 103-105.
//
//  Lazy-loaded on first access; reuses the private ReadDbcFile from the main
//  partial. A load-time count is logged so a bad offset is obvious (a correct
//  parse finds thousands of create-item spells; ~0 means the offset is wrong).
// ══════════════════════════════════════════════════════════════════════════

public partial class DbcService
{
    private Dictionary<uint, uint>? _spellCreatedItem;                        // spellId -> created item entry
    private Dictionary<uint, List<(uint spell, uint minRank)>>? _skillRecipes; // skillLine -> recipe spells (rank-ordered)
    private readonly object _profLock = new();

    // Gear-making primary professions (vanilla skill line IDs). Enchanting (333)
    // makes no equippable gear; Alchemy/Cooking/First Aid self-exclude via the
    // equippable filter, so they're simply not listed.
    private static readonly (uint id, string name)[] GEAR_PROFESSIONS =
    {
        (164u, "Blacksmithing"),
        (165u, "Leatherworking"),
        (197u, "Tailoring"),
        (202u, "Engineering"),
    };

    private const uint SPELL_EFFECT_CREATE_ITEM = 24u;

    private void EnsureProfessionData()
    {
        if (_spellCreatedItem != null && _skillRecipes != null) return;
        lock (_profLock)
        {
            if (_spellCreatedItem == null) LoadSpellCreatedItems();
            if (_skillRecipes == null) LoadSkillRecipes();
        }
    }

    private void LoadSpellCreatedItems()
    {
        var map = new Dictionary<uint, uint>();
        var path = Path.Combine(DbcPath, "Spell.dbc");
        if (File.Exists(path))
        {
            var (records, _, recordSize) = ReadDbcFile(path);
            int fieldCount = recordSize / 4;
            // guard against a short/unknown layout
            if (fieldCount > 105)
            {
                for (int i = 0; i < records.Length / recordSize; i++)
                {
                    int o = i * recordSize;
                    uint id = BitConverter.ToUInt32(records, o);
                    for (int e = 0; e < 3; e++)
                    {
                        uint eff = BitConverter.ToUInt32(records, o + (61 + e) * 4);
                        if (eff == SPELL_EFFECT_CREATE_ITEM)
                        {
                            uint item = BitConverter.ToUInt32(records, o + (103 + e) * 4);
                            if (item != 0) map[id] = item;
                            break;
                        }
                    }
                }
            }
        }
        _spellCreatedItem = map;
        LoadedCounts["SpellCreatedItem"] = map.Count;
        _logger.LogInformation("DbcService: {Count} spells create an item (effect 24). If this is ~0 the Spell.dbc effect offset is wrong.", map.Count);
    }

    private void LoadSkillRecipes()
    {
        var recipes = new Dictionary<uint, List<(uint spell, uint minRank)>>();
        var path = Path.Combine(DbcPath, "SkillLineAbility.dbc");
        if (File.Exists(path))
        {
            var (records, _, recordSize) = ReadDbcFile(path);
            int fieldCount = recordSize / 4;
            for (int i = 0; i < records.Length / recordSize; i++)
            {
                int o = i * recordSize;
                uint skill = BitConverter.ToUInt32(records, o + 1 * 4);
                uint spell = BitConverter.ToUInt32(records, o + 2 * 4);
                if (skill == 0 || spell == 0) continue;
                uint minRank = fieldCount > 7 ? BitConverter.ToUInt32(records, o + 7 * 4) : 0u;
                if (!recipes.TryGetValue(skill, out var list)) { list = new List<(uint, uint)>(); recipes[skill] = list; }
                list.Add((spell, minRank));
            }
            foreach (var list in recipes.Values)
                list.Sort((a, b) => a.minRank.CompareTo(b.minRank));
        }
        _skillRecipes = recipes;
        LoadedCounts["SkillLineAbility"] = recipes.Values.Sum(v => v.Count);
    }

    /// <summary>Item entry created by a craft spell (effect 24), or 0.</summary>
    public uint GetSpellCreatedItem(uint spellId)
    {
        EnsureProfessionData();
        return _spellCreatedItem!.TryGetValue(spellId, out var it) ? it : 0u;
    }

    /// <summary>Gear-making professions (skill line id + display name).</summary>
    public IReadOnlyList<(uint id, string name)> GetProfessions() => GEAR_PROFESSIONS;

    public string GetProfessionName(uint skillLineId)
        => GEAR_PROFESSIONS.FirstOrDefault(p => p.id == skillLineId).name ?? $"Skill {skillLineId}";

    /// <summary>Rank-ordered recipe spells (spellId, minRank) for a skill line.</summary>
    public IReadOnlyList<(uint spell, uint minRank)> GetProfessionRecipeSpells(uint skillLineId)
    {
        EnsureProfessionData();
        return _skillRecipes!.TryGetValue(skillLineId, out var list)
            ? list
            : new List<(uint, uint)>();
    }

    /// <summary>
    /// Rank-ordered (outputItemEntry, minRank) for a profession, de-duplicated by
    /// output item (lowest rank kept). Items still need a SQL join for name/class.
    /// </summary>
    public List<(uint itemEntry, uint minRank)> GetProfessionOutputs(uint skillLineId)
    {
        EnsureProfessionData();
        var seen = new HashSet<uint>();
        var outList = new List<(uint, uint)>();
        foreach (var (spell, rank) in GetProfessionRecipeSpells(skillLineId))
        {
            uint item = GetSpellCreatedItem(spell);
            if (item == 0 || !seen.Add(item)) continue;
            outList.Add((item, rank));
        }
        return outList;
    }
}
