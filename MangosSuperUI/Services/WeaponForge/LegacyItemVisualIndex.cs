using MangosSuperUI.Services.Mpq;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The <b>permanent glow a later client hangs on a weapon's display row</b>, which the import
/// pipeline used to throw away in silence.
///
/// TBC and WotLK give a weapon its always-on glow two different ways. One is a particle emitter
/// graph inside the M2 — that is what <see cref="ItemVisualSuggester.Suggest"/> reads. The other,
/// and the more common one, is <c>ItemDisplayInfo</c>'s <b>ItemVisual</b> field: an id into the
/// client's own <c>ItemVisuals.dbc</c>, whose five slots name <c>ItemVisualEffects.dbc</c> models
/// hung on the weapon's attachment points 0..4. Vanilla works exactly the same way (that is how
/// Ashbringer glows), so this one is transplantable — the forge just never looked at it.
///
/// Measured on the local clients: <b>890 of 7,515</b> browsable 2.4.3 weapon/shield display rows and
/// <b>1,356 of 10,062</b> 3.3.5a ones carry a real ItemVisual. Most point at ids 1–134, which 1.12
/// ships byte-identically (25 RedFlame_Low, 42 BlueGlow_Low, 107 PurpleGlow_Low…), so they copy
/// straight across; the later clients mostly <i>append</i> rows (137–169: SoulfrostGlow,
/// ExecutionerGlow, IcyEnchant, ShamanisticRage…) and those are matched to the nearest vanilla glow
/// by their effect-model stems — see <see cref="ItemVisualSuggester.MapLaterClientVisual"/>. Through
/// that mapping the forge reproduces 851 of the 890 TBC glows and 1,252 of the 1,356 WotLK ones; the
/// remainder are spell-cast animations with no permanent-glow equivalent.
///
/// Lifetime mirrors <see cref="LegacyMpqSource"/>: built lazily on first use, dropped when the
/// configured client path changes. Reading three small DBCs, so it is cheap and fully in memory.
/// </summary>
public sealed class LegacyItemVisualIndex
{
    // The ItemVisual column (vanilla field 22) is shifted to 23 by the second inventory icon in
    // later clients. That shift is detected by CONTENT in ItemDisplayInfoLayout.VisualField — the old
    // "FieldCount >= 25 ? 23 : 22" guess read stock 24-field 2.4.3 (which HAS the icon) at 22, one
    // column short, so TBC weapon glows resolved from the wrong column.

    private readonly Dictionary<uint, uint> _displayToVisual;   // display row → source ItemVisual id
    private readonly Dictionary<uint, string[]> _visualEffects; // source ItemVisual id → effect stems

    private LegacyItemVisualIndex(Dictionary<uint, uint> displayToVisual, Dictionary<uint, string[]> visualEffects)
    {
        _displayToVisual = displayToVisual;
        _visualEffects = visualEffects;
    }

    /// <summary>Rows in <c>ItemDisplayInfo</c> that name a non-zero ItemVisual (all item kinds, not
    /// just weapons) — the denominator for the coverage report.</summary>
    public int RowsWithVisual => _displayToVisual.Count;

    /// <summary>The source client's own ItemVisual for a display row, plus the effect-model stems it
    /// names. Id 0 ⇒ the row has no permanent glow.</summary>
    public (uint Id, IReadOnlyList<string> EffectStems) ForDisplayRow(uint displayRow)
    {
        if (displayRow == 0 || !_displayToVisual.TryGetValue(displayRow, out uint id) || id == 0)
            return (0, Array.Empty<string>());
        return (id, _visualEffects.TryGetValue(id, out var stems) ? stems : Array.Empty<string>());
    }

    /// <summary>Effect-model stems of a source ItemVisual id (empty when the id has no row — the
    /// later clients do ship display rows pointing at 0xFFFFFFFF and other dangling ids).</summary>
    public IReadOnlyList<string> EffectStems(uint visualId) =>
        _visualEffects.TryGetValue(visualId, out var stems) ? stems : Array.Empty<string>();

    /// <summary>Every (display row → visual id) pair, for the coverage report.</summary>
    public IReadOnlyDictionary<uint, uint> All => _displayToVisual;

    /// <summary>Read the three DBCs out of a mounted client. Never throws: a client missing any of
    /// them yields an empty index, which degrades to today's emitter-only behaviour.</summary>
    public static LegacyItemVisualIndex Build(Func<string, byte[]?> extract, ILogger logger, string label)
    {
        var displayToVisual = new Dictionary<uint, uint>();
        var visualEffects = new Dictionary<uint, string[]>();
        try
        {
            // ItemVisualEffects: id → model path ("Spells\PurpleGlow_Low.mdx").
            var effName = new Dictionary<uint, string>();
            var iveBytes = extract(@"DBFilesClient\ItemVisualEffects.dbc");
            if (iveBytes is { Length: > 0 })
            {
                var ive = DbcWriterService.ReadDbc(iveBytes, label + ":ItemVisualEffects");
                foreach (var r in ive.GetAllRows())
                    if (r.Length > 1) effName[r[0]] = Path.GetFileNameWithoutExtension(ive.ReadString(r[1]));
            }

            // ItemVisuals: id → up to five effect ids (one per attachment point 0..4).
            var ivBytes = extract(@"DBFilesClient\ItemVisuals.dbc");
            if (ivBytes is { Length: > 0 })
            {
                var iv = DbcWriterService.ReadDbc(ivBytes, label + ":ItemVisuals");
                foreach (var r in iv.GetAllRows())
                {
                    var stems = new List<string>();
                    for (int i = 1; i < iv.FieldCount && i < r.Length; i++)
                        if (r[i] != 0 && effName.TryGetValue(r[i], out var n) && n.Length > 0 && !stems.Contains(n, StringComparer.OrdinalIgnoreCase))
                            stems.Add(n);
                    visualEffects[r[0]] = stems.ToArray();
                }
            }

            // ItemDisplayInfo: display row → ItemVisual id. Both 0 and 0xFFFFFFFF mean "no glow"
            // (3.3.5a uses the latter on 249 weapon rows), and an id naming no ItemVisuals row is
            // dangling data — none of the three is a glow the forge failed to carry across.
            var idiBytes = extract(WeaponNaming.ItemDisplayInfoMember);
            if (idiBytes is { Length: > 0 })
            {
                var idi = DbcWriterService.ReadDbc(idiBytes, label + ":ItemDisplayInfo");
                int f = ItemDisplayInfoLayout.VisualField(idi);
                bool haveVisualRows = visualEffects.Count > 0;
                foreach (var r in idi.GetAllRows())
                {
                    if (f >= r.Length) continue;
                    uint v = r[f];
                    if (v == 0 || v == uint.MaxValue) continue;
                    if (haveVisualRows && !visualEffects.ContainsKey(v)) continue;
                    displayToVisual[r[0]] = v;
                }
            }
            logger.LogInformation("{Label}: ItemVisual index — {Rows} display rows carry a glow across {Visuals} visual definitions",
                label, displayToVisual.Count, visualEffects.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Label}: ItemVisual index unavailable — imports fall back to emitter-only glow suggestion", label);
        }
        return new LegacyItemVisualIndex(displayToVisual, visualEffects);
    }
}
