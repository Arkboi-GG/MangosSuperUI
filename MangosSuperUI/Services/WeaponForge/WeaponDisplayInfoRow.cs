using MangosSuperUI.Services;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Builds an explicit, purpose-built ItemDisplayInfo.dbc row for a generated weapon (WEAPON_GEN.md
/// §2.2). Every one of the 23 fields is set deliberately rather than inherited from a cloned donor,
/// so a weapon can never silently pick up a donor's ModelName2, body-atlas texture, spell/item
/// visual, or geoset state. Field 9 (flags) and field 10 (SpellVisualID) are BOTH zero for v1 — the
/// raw installed-DBC evidence in WEAPON_GEN.md §2.2 (field 9 nonzero only in 11 armor rows; field 10
/// carries ranged SpellVisual values) settles that field 9 is flags and field 10 is SpellVisualID.
/// </summary>
public static class WeaponDisplayInfoRow
{
    /// <summary>Vanilla ItemDisplayInfo: 23 uint32 fields, 92 bytes.</summary>
    public const int FieldCount = 23;
    public const int RecordSize = 92;

    // Field indices (see WEAPON_GEN.md §2.2 table).
    public const int F_Id = 0;
    public const int F_ModelName1 = 1;
    public const int F_ModelName2 = 2;
    public const int F_TextureName1 = 3;
    public const int F_TextureName2 = 4;
    public const int F_InventoryIcon = 5;
    public const int F_GeosetGroup0 = 6;
    public const int F_GeosetGroup1 = 7;
    public const int F_GeosetGroup2 = 8;
    public const int F_Flags = 9;
    public const int F_SpellVisualId = 10;
    public const int F_GroupSoundIndex = 11;
    public const int F_HelmetGeosetVis0 = 12;
    public const int F_HelmetGeosetVis1 = 13;
    public const int F_Texture0 = 14; // body-atlas components 0..7 → fields 14..21
    public const int F_ItemVisual = 22;

    /// <summary>
    /// Construct the 23-field row against <paramref name="dbc"/> (which owns string-offset
    /// allocation) and append it. Returns the row for inspection/validation. Throws if the target
    /// DBC is not the 92-byte ItemDisplayInfo schema — a guard against writing into the wrong file.
    /// </summary>
    public static uint[] BuildAndAdd(DbcWriterService dbc, WeaponDisplayInfoParams p)
    {
        if (dbc.RecordSize != RecordSize)
            throw new InvalidOperationException(
                $"Target DBC record size {dbc.RecordSize} != ItemDisplayInfo {RecordSize}; refusing to write a weapon display row into it.");

        var row = new uint[FieldCount];
        row[F_Id] = p.DisplayId;

        // String references. AddString dedupes and returns the offset assigned at Write time; an
        // empty string maps to offset 0 (the DBC's mandatory empty entry).
        row[F_ModelName1] = dbc.AddString(WeaponNaming.DbcModelName(p.ModelIndex));
        row[F_ModelName2] = 0; // ordinary weapon: no second model
        row[F_TextureName1] = dbc.AddString(WeaponNaming.DbcTextureName(p.ModelIndex, p.Variant));
        row[F_TextureName2] = 0;
        row[F_InventoryIcon] = string.IsNullOrEmpty(p.IconStem) ? 0u : dbc.AddString(p.IconStem);

        // Geoset groups, helmet visibility, body-atlas textures, flags, spell/item visuals all zero.
        row[F_GeosetGroup0] = row[F_GeosetGroup1] = row[F_GeosetGroup2] = 0;
        row[F_Flags] = 0;
        row[F_SpellVisualId] = 0;
        row[F_GroupSoundIndex] = p.GroupSoundIndex; // preserve a simple-sword sound group
        row[F_HelmetGeosetVis0] = row[F_HelmetGeosetVis1] = 0;
        for (int i = F_Texture0; i < F_Texture0 + 8; i++) row[i] = 0;
        row[F_ItemVisual] = p.ItemVisual; // 0 until enchant effects are explicitly implemented

        dbc.AddRow(row);
        return row;
    }

    /// <summary>Validate a built row against the v1 contract (WEAPON_GEN.md §7.3). String presence is
    /// checked against the writer so ModelName1 must resolve to a non-empty string and ModelName2
    /// must be empty.</summary>
    public static void Validate(DbcWriterService dbc, uint[] row, ForgeDiagnostics d)
    {
        if (row.Length != FieldCount)
        { d.Error("dbc.row.width", $"Row has {row.Length} fields, expected {FieldCount}."); return; }

        if (row[F_ModelName1] == 0 || string.IsNullOrEmpty(dbc.ReadString(row[F_ModelName1])))
            d.Error("dbc.modelname1.missing", "ModelName1 is empty.");
        if (row[F_ModelName2] != 0 && !string.IsNullOrEmpty(dbc.ReadString(row[F_ModelName2])))
            d.Error("dbc.modelname2.present", "ModelName2 must be empty for an ordinary weapon.");
        if (row[F_TextureName1] == 0 || string.IsNullOrEmpty(dbc.ReadString(row[F_TextureName1])))
            d.Error("dbc.texturename1.missing", "TextureName1 is empty.");
        if (row[F_Flags] != 0) d.Error("dbc.flags.nonzero", $"Field 9 (flags) must be 0 for v1 (got {row[F_Flags]}).");
        if (row[F_SpellVisualId] != 0) d.Error("dbc.spellvisual.nonzero", $"Field 10 (SpellVisualID) must be 0 for v1 (got {row[F_SpellVisualId]}).");
        if (row[F_ItemVisual] != 0) d.Error("dbc.itemvisual.nonzero", $"ItemVisual must be 0 for v1 (got {row[F_ItemVisual]}).");
        for (int i = F_Texture0; i < F_Texture0 + 8; i++)
            if (row[i] != 0) d.Warn("dbc.bodytexture.present", $"Body-atlas texture field {i} is non-zero; weapons should leave these empty.");
    }
}

/// <summary>Parameters for one weapon ItemDisplayInfo row.</summary>
public sealed class WeaponDisplayInfoParams
{
    public required uint DisplayId { get; init; }
    public required int ModelIndex { get; init; }
    public int Variant { get; init; } = 1;
    /// <summary>Interface\Icons stem to reuse initially; empty → no icon reference.</summary>
    public string IconStem { get; init; } = "";
    /// <summary>GroupSoundIndex to preserve (a simple-sword donor value). 0 is acceptable for v1.</summary>
    public uint GroupSoundIndex { get; init; } = 0;
    public uint ItemVisual { get; init; } = 0;
}
