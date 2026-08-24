namespace MangosSuperUI.Services;

/// <summary>
/// Where the variable-width columns of a later-client <c>ItemDisplayInfo.dbc</c> actually sit.
///
/// === The problem this solves ===
/// Post-vanilla clients insert a SECOND inventory icon (a stringref) at field 6. That single
/// insertion shifts <c>geosetGroup[3]</c>, <c>groupSoundIndex</c>, <c>helmetGeosetVis[2]</c>, all
/// eight <c>m_texture[]</c> component stems, and <c>itemVisual</c> up by one column. The correct
/// "component base" (index of <c>m_texture[0]</c>) is therefore 14 without the icon and 15 with it.
///
/// The bug this replaces inferred the shift from the TOTAL field count
/// (<c>FieldCount &gt;= 25 ? 15 : 14</c>). That is the wrong signal, because the field count is also
/// moved by an INDEPENDENT, trailing column (<c>particleColorID</c>), so it is ambiguous:
///
///   client                              fields  2nd icon?  correct base   count-heuristic
///   1.12                                  23      no          14              14  ✓
///   2.4.3 (stock)                         24     YES          15              14  ✗  ← broke TBC
///   3.3.5a (stock)                        25     YES          15              15  ✓
///   3.3.5a, particleColorID stripped      24     YES          15              14  ✗  ← broke WotLK
///
/// So a 24-field record was read one column short: geosetGroup came from
/// [icon2, geoset0, geoset1] instead of [geoset0, geoset1, geoset2], and every component stem
/// resolved to the wrong (or an empty) string. That is exactly the "TBC/WotLK gear not 1:1" symptom.
///
/// === The fix ===
/// Decide by CONTENT, not by field count. With the icon absent, field 6 is <c>geosetGroup[0]</c> —
/// a tiny variant int (single digit). With the icon present, field 6 is a stringref: a large offset
/// into the multi-KB icon string block that resolves to a non-empty name. One such row proves the
/// icon is there. This is unambiguous for the same 24-field record either way.
/// </summary>
public static class ItemDisplayInfoLayout
{
    /// <summary>Vanilla single-icon layout: geosetGroup at 6-8, m_texture at 14-21, itemVisual at 22.</summary>
    public const int VanillaComponentBase = 14;

    /// <summary>Post-vanilla layout with the second inventory icon at field 6 — everything ≥ 6 shifts +1.</summary>
    public const int SecondIconComponentBase = 15;

    /// <summary>
    /// Index of <c>m_texture[0]</c> (the "component base"): 14 without the second inventory icon,
    /// 15 with it. Content-based so a 24-field record resolves correctly whether the 24th column is
    /// the second icon (base 15) or a trailing particleColorID with no icon (base 14).
    /// </summary>
    public static int DetectComponentBase(DbcWriterService? dbc)
    {
        if (dbc is null) return VanillaComponentBase;

        // A geoset variant is a single-digit int; the second inventory icon is a large offset into
        // the icon string block that resolves to a non-empty name. Any row where field 6 is a large,
        // resolvable stringref proves the icon sits there → base 15. A real client's icon column
        // always carries some large offsets, so one pass over the rows is decisive.
        const uint GeosetVariantCeiling = 64;   // real geoset groups never approach this
        foreach (var row in dbc.GetAllRows())
            if (row.Length > 6 && row[6] > GeosetVariantCeiling && dbc.ReadString(row[6]).Length > 0)
                return SecondIconComponentBase;

        // Field 6 never looked like a stringref → no second icon. Fall back to the field-count hint
        // only for the pathological all-empty-icon case, so a genuine 25-field file still maps.
        return dbc.FieldCount >= 25 ? SecondIconComponentBase : VanillaComponentBase;
    }

    /// <summary>Column holding the <c>itemVisual</c> id — vanilla 22, shifted to 23 by the second icon.</summary>
    public static int VisualField(DbcWriterService? dbc) => DetectComponentBase(dbc) + 8;
}
