using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// What to do with a later-client armor skin's alpha channel when the piece is re-emitted for 1.12.
///
/// TBC Tier 5/6 plate (Onslaught, Lightbringer, ...) helms and spaulders draw one submesh twice:
/// a layer-0 OPAQUE pass (diffuse + ARMORREFLECT env map, two texture units) and a layer-1
/// ALPHA-BLENDED pass (blend 2, no depth write) that redraws the same diffuse through the skin's
/// alpha channel. That alpha channel is a shininess mask: alpha high = matte (diffuse covers the
/// reflection), alpha low = shiny (reflection shows). The Onslaught helm skin has no fully-opaque
/// texel and 90% of it sits below 50% alpha.
///
/// In the 1.12 world scene that composites correctly, but the character frame (paper doll) treats
/// whatever alpha the item's texture writes as COVERAGE, so a masked helm is drawn see-through in
/// the portrait while being solid on the model — the classic "alpha channel in the BLP" artifact of
/// ported gear. 1.12 has no way to keep the mask AND write opaque coverage, so the import trades
/// the mask away:
///   1. every alpha-blended layer that only redraws the skin over an opaque lower-layer base of the
///      same submesh is dropped (the reflection then shows everywhere, which for this art — mostly
///      shiny by the mask's own numbers — is the closer of the two solid looks);
///   2. when no remaining pass still depends on the skin's alpha (alpha-key cutouts, translucent
///      pieces, add-alpha glows), the caller flattens the packaged skin to fully opaque.
/// Both steps are gated on the skin actually carrying an alpha channel; an opaque skin is untouched.
/// </summary>
public static class ImportedSkinAlphaPolicy
{
    /// <summary>The replaceable display texture (the DBC skin) is always extractor texture slot 0.</summary>
    public const int SkinSlot = 0;

    public sealed record Result(IReadOnlyList<WeaponPass> Passes, int StrippedMaskPasses, bool SkinAlphaRequired);

    /// <summary>Drop the reflection-mask overlays and report whether any surviving pass still needs
    /// the skin's alpha channel. Pure; the input list is not modified.</summary>
    public static Result Apply(IReadOnlyList<WeaponPass> passes)
    {
        var kept = new List<WeaponPass>(passes.Count);
        int stripped = 0;
        foreach (var pass in passes)
        {
            if (IsReflectionMaskOverlay(pass, passes)) { stripped++; continue; }
            kept.Add(pass);
        }
        bool required = kept.Any(p => SamplesSkin(p) && p.BlendMode is 1 or 2 or 4);
        return new Result(kept, stripped, required);
    }

    /// <summary>A layer-above-zero alpha-blended pass sampling ONLY the skin, over a lower-layer
    /// opaque pass of the same submesh that also samples the skin.</summary>
    public static bool IsReflectionMaskOverlay(WeaponPass pass, IReadOnlyList<WeaponPass> all)
    {
        if (pass.Layer <= 0 || pass.BlendMode != 2) return false;
        if (!Bindings(pass).All(b => b.TextureSlot == SkinSlot)) return false;
        return all.Any(q => !ReferenceEquals(q, pass)
                            && q.SubmeshSlot == pass.SubmeshSlot
                            && q.Layer < pass.Layer
                            && q.BlendMode == 0
                            && SamplesSkin(q));
    }

    /// <summary>The same mesh with a different pass list; every other attribute is shared.</summary>
    public static RigidWeaponMesh WithPasses(RigidWeaponMesh mesh, IReadOnlyList<WeaponPass> passes) => new()
    {
        Positions = mesh.Positions,
        Normals = mesh.Normals,
        Uv0 = mesh.Uv0,
        Uv1 = mesh.Uv1,
        Indices = mesh.Indices,
        VertexIds = mesh.VertexIds,
        Material = mesh.Material,
        TriangleRegionIds = mesh.TriangleRegionIds,
        Normalization = mesh.Normalization,
        SubmeshRanges = mesh.SubmeshRanges,
        Passes = passes,
        TextureSlots = mesh.TextureSlots,
    };

    /// <summary>BLP2 header byte 9 is the alpha depth; 0 means the file carries no alpha channel.</summary>
    public static bool BlpHasAlphaChannel(byte[]? blp) => blp is { Length: > 11 } && blp[9] != 0;

    private static bool SamplesSkin(WeaponPass pass) => Bindings(pass).Any(b => b.TextureSlot == SkinSlot);

    private static IEnumerable<WeaponTextureBinding> Bindings(WeaponPass pass)
        => pass.TextureBindings is { Count: > 0 } bindings
            ? bindings
            : new[] { new WeaponTextureBinding { TextureSlot = pass.TextureSlot } };
}
