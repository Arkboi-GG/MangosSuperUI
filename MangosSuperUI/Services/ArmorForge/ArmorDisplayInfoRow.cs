using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// Builds an explicit ItemDisplayInfo.dbc row for a forged armor piece — the armor-side sibling of
/// <see cref="WeaponDisplayInfoRow"/>. Where the weapon builder deliberately ZEROS the geoset
/// groups (6-8), helmet-visibility (12-13) and body-atlas component textures (14-21), the armor
/// builder is the one place those get filled, because they are exactly what makes armor visible:
///
///   • MODELLED (helm/shoulder): ModelName1 (+ModelName2 mirror for shoulders), TextureName1
///     (+TextureName2), the geoset groups, and — for helms — the two helmet hair/facial-hide masks.
///   • PAINTED (chest/legs/gloves/…): no model; the bare component stems go into fields 14+slot, and
///     any geoset groups (glove/boot/belt style variants) are carried across from the source row.
///   • CLOAK: TextureName1 only (the client paints the built-in cape geoset with it).
///
/// The field layout is shared with <see cref="WeaponDisplayInfoRow"/> (23 uint32 fields, 92 bytes) —
/// the same ItemDisplayInfo.dbc schema.
/// </summary>
public static class ArmorDisplayInfoRow
{
    public const int FieldCount = WeaponDisplayInfoRow.FieldCount; // 23
    public const int RecordSize = WeaponDisplayInfoRow.RecordSize; // 92

    /// <summary>Construct the 23-field row against <paramref name="dbc"/> and append it.</summary>
    public static uint[] BuildAndAdd(DbcWriterService dbc, ArmorDisplayInfoParams p)
    {
        if (dbc.RecordSize != RecordSize)
            throw new InvalidOperationException(
                $"Target DBC record size {dbc.RecordSize} != ItemDisplayInfo {RecordSize}; refusing to write an armor display row into it.");

        var row = new uint[FieldCount];
        row[WeaponDisplayInfoRow.F_Id] = p.DisplayId;

        // Models + model textures (modelled + cloak). Shoulders are an L/R PAIR of distinct files
        // (stock: ModelName1=LShoulder_X.mdx, ModelName2=RShoulder_X.mdx) sharing ONE texture
        // (TextureName1 == TextureName2); helms have a single logical model (the client appends the
        // race/gender suffix itself) and no second model. Measured on the local 1.12 client.
        uint modelRef = string.IsNullOrEmpty(p.ModelName) ? 0u : dbc.AddString(p.ModelName!);
        row[WeaponDisplayInfoRow.F_ModelName1] = modelRef;
        row[WeaponDisplayInfoRow.F_ModelName2] = string.IsNullOrEmpty(p.ModelName2) ? 0u : dbc.AddString(p.ModelName2!);

        uint texRef = string.IsNullOrEmpty(p.TextureName) ? 0u : dbc.AddString(p.TextureName!);
        row[WeaponDisplayInfoRow.F_TextureName1] = texRef;
        row[WeaponDisplayInfoRow.F_TextureName2] = string.IsNullOrEmpty(p.ModelName2) ? 0u : texRef;

        row[WeaponDisplayInfoRow.F_InventoryIcon] =
            string.IsNullOrEmpty(p.IconStem) ? 0u : dbc.AddString(p.IconStem);

        // Geoset groups (glove/boot/belt/sleeve visibility variants; helm hood shapes).
        row[WeaponDisplayInfoRow.F_GeosetGroup0] = unchecked((uint)p.GeosetGroup0);
        row[WeaponDisplayInfoRow.F_GeosetGroup1] = unchecked((uint)p.GeosetGroup1);
        row[WeaponDisplayInfoRow.F_GeosetGroup2] = unchecked((uint)p.GeosetGroup2);

        row[WeaponDisplayInfoRow.F_Flags] = 0;
        row[WeaponDisplayInfoRow.F_SpellVisualId] = 0;
        row[WeaponDisplayInfoRow.F_GroupSoundIndex] = p.GroupSoundIndex;

        // Helmet hair/facial-hide masks (helms only; carried from the source row).
        row[WeaponDisplayInfoRow.F_HelmetGeosetVis0] = p.HelmetVis0;
        row[WeaponDisplayInfoRow.F_HelmetGeosetVis1] = p.HelmetVis1;

        // Body-atlas component textures (painted pieces). Bare stems; the client prepends the
        // TextureComponents subdir and appends the gender suffix.
        for (int slot = 0; slot < 8; slot++)
        {
            int field = WeaponDisplayInfoRow.F_Texture0 + slot;
            row[field] = (p.ComponentStems != null && p.ComponentStems.TryGetValue(slot, out var stem)
                          && !string.IsNullOrEmpty(stem))
                ? dbc.AddString(stem)
                : 0u;
        }

        row[WeaponDisplayInfoRow.F_ItemVisual] = 0;

        dbc.AddRow(row);
        return row;
    }

    /// <summary>Validate a built armor row. The checks are the inverse of the weapon validator: a
    /// modelled piece must have a model + texture; a painted piece must paint at least one component;
    /// a cloak must have a texture.</summary>
    public static void Validate(DbcWriterService dbc, uint[] row, ArmorRenderKind kind, ForgeDiagnostics d)
    {
        if (row.Length != FieldCount)
        { d.Error("armor.dbc.row.width", $"Row has {row.Length} fields, expected {FieldCount}."); return; }

        bool hasModel = row[WeaponDisplayInfoRow.F_ModelName1] != 0
            && !string.IsNullOrEmpty(dbc.ReadString(row[WeaponDisplayInfoRow.F_ModelName1]));
        bool hasTexture = row[WeaponDisplayInfoRow.F_TextureName1] != 0
            && !string.IsNullOrEmpty(dbc.ReadString(row[WeaponDisplayInfoRow.F_TextureName1]));
        bool hasComponent = false;
        for (int slot = 0; slot < 8; slot++)
            if (row[WeaponDisplayInfoRow.F_Texture0 + slot] != 0) { hasComponent = true; break; }

        switch (kind)
        {
            case ArmorRenderKind.Modelled:
                if (!hasModel) d.Error("armor.dbc.model.missing", "Modelled armor (helm/shoulder) has no ModelName1.");
                if (!hasTexture) d.Error("armor.dbc.texture.missing", "Modelled armor has no TextureName1.");
                break;
            case ArmorRenderKind.Painted:
                if (!hasComponent)
                    d.Error("armor.dbc.component.missing", "Painted armor sets none of the eight body-atlas component textures.");
                break;
            case ArmorRenderKind.Cloak:
                if (!hasTexture) d.Error("armor.dbc.cloak.missing", "Cloak has no TextureName1.");
                break;
        }

        if (row[WeaponDisplayInfoRow.F_Flags] != 0)
            d.Warn("armor.dbc.flags", $"Field 9 (flags) is {row[WeaponDisplayInfoRow.F_Flags]}; expected 0.");
    }
}

/// <summary>Parameters for one armor ItemDisplayInfo row.</summary>
public sealed class ArmorDisplayInfoParams
{
    public required uint DisplayId { get; init; }

    /// <summary>ModelName1 value (logical, carries .mdx), e.g. "SUI_A_0001.mdx" (helm — client appends
    /// the race/gender suffix) or "SUI_A_0001_L.mdx" (left shoulder). Empty for painted.</summary>
    public string? ModelName { get; init; }
    /// <summary>ModelName2 — the RIGHT shoulder file ("SUI_A_0001_R.mdx"); empty for everything else.
    /// When set, TextureName2 mirrors TextureName1 (both pads share one texture, as stock rows do).</summary>
    public string? ModelName2 { get; init; }
    /// <summary>Bare model-texture stem (modelled + cloak). Empty for painted.</summary>
    public string? TextureName { get; init; }

    public string IconStem { get; init; } = "";
    public uint GroupSoundIndex { get; init; } = 0;

    public int GeosetGroup0 { get; init; }
    public int GeosetGroup1 { get; init; }
    public int GeosetGroup2 { get; init; }

    public uint HelmetVis0 { get; init; }
    public uint HelmetVis1 { get; init; }

    /// <summary>slot (0..7) → bare component-texture stem, for painted pieces.</summary>
    public Dictionary<int, string>? ComponentStems { get; init; }
}
