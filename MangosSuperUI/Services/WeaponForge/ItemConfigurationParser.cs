using System.Text.Json;
using System.Text.Json.Serialization;
using MangosSuperUI.Services;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Shared intake for the optional typed Vanilla gameplay contract that both the Weapon Forge and
/// the Armor Forge carry as an <c>itemConfig</c> JSON field. Deserializes fail-closed (unknown
/// properties are rejected so a typo can never silently produce a donor-default item), translates
/// the request into validated <c>item_template</c> column overrides via
/// <see cref="VanillaItemBuildConfigurationTranslator"/>, and re-checks every configured spell slot
/// against the complete native stock-item slot catalog. The allowed equip-slot set is passed by the
/// caller (weapon slots vs <see cref="VanillaItemBuildConfigurationTranslator.ArmorInventoryTypes"/>)
/// so one intake path serves both forges identically.
/// </summary>
public sealed class ItemConfigurationParser
{
    public const int MaxItemConfigurationChars = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly DbcService _dbc;
    private readonly VanillaItemSpellCatalog _itemSpells;
    private readonly ILogger<ItemConfigurationParser> _logger;

    public ItemConfigurationParser(DbcService dbc, VanillaItemSpellCatalog itemSpells,
        ILogger<ItemConfigurationParser> logger)
    {
        _dbc = dbc;
        _itemSpells = itemSpells;
        _logger = logger;
    }

    /// <summary>Parse and validate an <c>itemConfig</c> JSON payload. A null/blank payload is a
    /// success with no overrides. <paramref name="allowedInventoryTypes"/> null means the default
    /// weapon slot set.</summary>
    public async Task<(ValidatedVanillaItemBuildConfiguration? Configuration, IReadOnlyList<string> Errors)>
        ParseAsync(string? itemConfig, IReadOnlySet<int>? allowedInventoryTypes, string? inventoryTypeError,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemConfig))
            return (null, Array.Empty<string>());

        if (itemConfig.Length > MaxItemConfigurationChars)
            return (null, [$"itemConfig exceeds the {MaxItemConfigurationChars:N0}-character limit."]);

        VanillaItemBuildConfiguration? request;
        try
        {
            request = JsonSerializer.Deserialize<VanillaItemBuildConfiguration>(itemConfig, JsonOptions);
        }
        catch (JsonException ex)
        {
            string location = ex.Path is { Length: > 0 } ? $" at {ex.Path}" : "";
            return (null, [$"itemConfig is not valid JSON{location}: {ex.Message}"]);
        }

        if (request is null)
            return (null, ["itemConfig must be a JSON object, not null."]);

        Func<uint, bool>? spellExists = _dbc.IsLoaded && _dbc.AllSpellEntries.Count > 0
            ? spellId => _dbc.AllSpellEntries.ContainsKey(spellId)
            : null;
        Func<int, bool>? requiredSkillExists = _dbc.IsLoaded && _dbc.SkillLineIds.Count > 0
            ? id => _dbc.SkillLineIds.Contains((uint)id)
            : null;
        Func<int, bool>? reputationFactionExists = _dbc.IsLoaded && _dbc.FactionIds.Count > 0
            ? id => _dbc.FactionIds.Contains((uint)id)
            : null;

        if (!VanillaItemBuildConfigurationTranslator.TryTranslate(
                request, spellExists, requiredSkillExists, reputationFactionExists,
                out var validated, out var errors,
                allowedInventoryTypes, inventoryTypeError))
            return (null, errors);

        if (request.Spells is { Count: > 0 })
        {
            IReadOnlyList<NativeItemSpellUsage> nativeUsage;
            try
            {
                nativeUsage = await _itemSpells.GetUsageAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ItemConfigurationParser: could not validate native item spell effects");
                return (null, ["Item spell effects cannot be validated against stock Vanilla items right now."]);
            }

            var nativeErrors = new List<string>();
            for (int i = 0; i < request.Spells.Count; i++)
            {
                var spell = request.Spells[i]!; // structural/null validation already passed in TryTranslate
                uint spellId = (uint)spell.SpellId!.Value;
                int trigger = spell.Trigger!.Value;
                int charges = spell.Charges ?? 0;
                float ppmRate = spell.PpmRate ?? 0;
                int cooldownMs = spell.CooldownMs ?? -1;
                int category = spell.Category ?? 0;
                int categoryCooldownMs = spell.CategoryCooldownMs ?? -1;
                bool exactStockSlot = nativeUsage.Any(x =>
                    x.SpellId == spellId &&
                    x.TriggerValue == trigger &&
                    x.Charges == charges &&
                    x.PpmRate == ppmRate &&
                    x.CooldownMs == cooldownMs &&
                    x.Category == category &&
                    x.CategoryCooldownMs == categoryCooldownMs);
                if (!exactStockSlot)
                    nativeErrors.Add($"spells[{i}] must preserve a complete stock Vanilla item-spell slot; " +
                        $"spell {spellId} is not available with that {TriggerLabel(trigger)}, charges, PPM, and cooldown combination.");
            }
            if (nativeErrors.Count > 0)
                return (null, nativeErrors);
        }

        return (validated, Array.Empty<string>());
    }

    public static string TriggerLabel(int trigger) => trigger switch
    {
        0 => "Use",
        1 => "On Equip",
        2 => "Chance on Hit",
        _ => $"trigger {trigger}"
    };
}
