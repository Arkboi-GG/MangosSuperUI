using System.Text.Json;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The SHIPPED TBC item catalog: entry → name / class / subclass / display id / quality / basic
/// stats for every class-2 weapon AND class-4 armor/shield in the open-source cmangos tbc-db
/// world database, baked into <c>wwwroot/data/tbc-item-catalog.json</c> at development time.
/// Item names never exist in the client MPQs (the client learns them from the server's item
/// query), so without this file a TBC browse could only show model stems. Shipping the list means
/// any SuperUI user gets real item names with nothing but their own TBC client files — no live
/// TBC database required. Armor and shields ride along for the future armor/shield import; only
/// weapons are forgeable today.
///
/// The runtime join is: catalog item.displayId → the user's own mounted TBC ItemDisplayInfo row
/// (<see cref="TbcMpqSource.WeaponIndex"/>) → model + texture members. The catalog also maps each
/// TBC weapon subclass onto a Forge weapon-type profile so the import pre-selects the right family.
/// Loaded lazily once; a missing/corrupt file degrades to the model-stem browse, never an error.
/// </summary>
public sealed class TbcItemCatalog
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TbcItemCatalog> _logger;
    private readonly object _lock = new();
    private IReadOnlyList<TbcItemInfo>? _items;
    private Dictionary<uint, List<TbcItemInfo>>? _byDisplay;

    public const string CatalogWebPath = "data/tbc-item-catalog.json";

    public TbcItemCatalog(IWebHostEnvironment env, ILogger<TbcItemCatalog> logger)
    {
        _env = env;
        _logger = logger;
    }

    public IReadOnlyList<TbcItemInfo> Items
    {
        get
        {
            if (_items is not null) return _items;
            lock (_lock)
            {
                _items ??= Load();
                return _items;
            }
        }
    }

    /// <summary>Items grouped by their TBC display id, for the join against the mounted archives.</summary>
    public IReadOnlyDictionary<uint, List<TbcItemInfo>> ByDisplayId
    {
        get
        {
            if (_byDisplay is not null) return _byDisplay;
            lock (_lock)
            {
                if (_byDisplay is null)
                {
                    var map = new Dictionary<uint, List<TbcItemInfo>>();
                    foreach (var item in Items)
                    {
                        if (!map.TryGetValue(item.DisplayId, out var list))
                            map[item.DisplayId] = list = new List<TbcItemInfo>();
                        list.Add(item);
                    }
                    _byDisplay = map;
                }
                return _byDisplay;
            }
        }
    }

    public TbcItemInfo? FindByEntry(uint entry) => Items.FirstOrDefault(i => i.Entry == entry);

    private IReadOnlyList<TbcItemInfo> Load()
    {
        var path = Path.Combine(_env.WebRootPath, CatalogWebPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            _logger.LogWarning("TbcItemCatalog: {Path} not found — TBC browse falls back to model stems", path);
            return Array.Empty<TbcItemInfo>();
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var items = new List<TbcItemInfo>();
            foreach (var row in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                // fields: entry, name, class, subclass, displayId, quality, inventoryType,
                //         itemLevel, requiredLevel, delayMs, dmgMin, dmgMax, sheath
                items.Add(new TbcItemInfo
                {
                    Entry = row[0].GetUInt32(),
                    Name = row[1].GetString() ?? "",
                    ItemClass = row[2].GetInt32(),
                    Subclass = row[3].GetInt32(),
                    DisplayId = row[4].GetUInt32(),
                    Quality = row[5].GetInt32(),
                    InventoryType = row[6].GetInt32(),
                    ItemLevel = row[7].GetInt32(),
                    RequiredLevel = row[8].GetInt32(),
                    DelayMs = row[9].GetInt32(),
                    DmgMin = row[10].GetSingle(),
                    DmgMax = row[11].GetSingle(),
                    Sheath = row[12].GetInt32(),
                });
            }
            _logger.LogInformation("TbcItemCatalog: loaded {Count} TBC items (weapons + armor/shields) from the shipped catalog", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TbcItemCatalog: could not read {Path}: {Err} — TBC browse falls back to model stems",
                path, ex.Message);
            return Array.Empty<TbcItemInfo>();
        }
    }

    /// <summary>TBC class-2 subclass → Forge weapon-family key. Subclasses the static pipeline
    /// cannot represent (bows/guns/wands/thrown/fishing poles are animated or paired) return null
    /// and are excluded from the browse.</summary>
    public static string? TypeKeyForSubclass(int subclass) => subclass switch
    {
        0 => "axe1h",
        1 => "axe2h",
        4 => "mace1h",
        5 => "mace2h",
        6 => "polearm",
        7 => "sword1h",
        8 => "sword2h",
        10 => "staff",
        14 => "sword1h",   // Miscellaneous (brooms etc.) — closest static 1H contract
        15 => "dagger",
        _ => null,
    };
}

/// <summary>One shipped TBC item (from the open-source world DB). Class 2 = weapon,
/// class 4 = armor (subclass 6 = shield).</summary>
public sealed record TbcItemInfo
{
    public required uint Entry { get; init; }
    public required string Name { get; init; }
    public required int ItemClass { get; init; }
    public required uint DisplayId { get; init; }
    public required int Quality { get; init; }
    public required int Subclass { get; init; }
    public required int InventoryType { get; init; }
    public required int ItemLevel { get; init; }
    public required int RequiredLevel { get; init; }
    public required int DelayMs { get; init; }
    public required float DmgMin { get; init; }
    public required float DmgMax { get; init; }
    public required int Sheath { get; init; }
}
