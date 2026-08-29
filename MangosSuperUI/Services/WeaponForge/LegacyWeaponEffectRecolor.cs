namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Recolored effect assets for a later-client fidelity import. Effect texture slot N maps to
/// list index N-1; an empty BLP entry deliberately makes the builder use the recolored PNG for
/// that slot while untouched entries retain their source BLP byte-for-byte.
/// </summary>
internal sealed record LegacyWeaponEffectTint(
    List<byte[]>? Pngs,
    List<byte[]>? Blps,
    IReadOnlyList<int> TextureSlots);

/// <summary>
/// Select and hue-shift only later-client texture slots used exclusively by compositing passes.
/// This is the TBC/WotLK rigid-render-graph path (for example the Warglaive's Blend-4,
/// environment-mapped ArmorReflect3 shell), not the source-preserved Vanilla weapon path.
/// </summary>
internal static class LegacyWeaponEffectRecolor
{
    internal static IReadOnlyList<int> SelectEligibleTextureSlots(
        RigidWeaponMesh mesh,
        int effectTextureCount)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (effectTextureCount < 0)
            throw new ArgumentOutOfRangeException(nameof(effectTextureCount));

        var uses = new Dictionary<int, (bool Used, bool EveryUseComposites)>();
        for (int slot = 1; slot <= effectTextureCount; slot++)
            uses[slot] = (false, true);

        foreach (WeaponPass pass in mesh.Passes ?? Array.Empty<WeaponPass>())
        {
            IReadOnlyList<int> slots = pass.TextureBindings is { Count: > 0 } bindings
                ? bindings.Select(binding => binding.TextureSlot).Distinct().ToArray()
                : [pass.TextureSlot];

            foreach (int slot in slots)
            {
                if (slot <= 0) continue; // slot 0 is the display/base skin
                if (slot > effectTextureCount)
                    throw new InvalidOperationException(
                        $"Render pass references effect texture slot {slot}, but only " +
                        $"{effectTextureCount} effect texture(s) were loaded.");

                var use = uses[slot];
                uses[slot] = (true, use.EveryUseComposites && pass.BlendMode >= 3);
            }
        }

        return uses
            .Where(pair => pair.Value.Used && pair.Value.EveryUseComposites)
            .Select(pair => pair.Key)
            .OrderBy(slot => slot)
            .ToArray();
    }

    internal static LegacyWeaponEffectTint Apply(
        RigidWeaponMesh mesh,
        IReadOnlyList<byte[]>? effectPngs,
        IReadOnlyList<byte[]>? effectBlps,
        float targetHueDegrees,
        float targetSaturation)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!float.IsFinite(targetHueDegrees) || !float.IsFinite(targetSaturation))
            throw new ArgumentException("Effect tint hue and saturation must be finite.");

        if (effectPngs is null || effectPngs.Count == 0)
            return new LegacyWeaponEffectTint(
                effectPngs?.ToList(), effectBlps?.ToList(), Array.Empty<int>());
        if (effectBlps is not null && effectBlps.Count != effectPngs.Count)
            throw new InvalidOperationException(
                $"Effect PNG/BLP counts differ ({effectPngs.Count} PNG, {effectBlps.Count} BLP).");

        IReadOnlyList<int> selected = SelectEligibleTextureSlots(mesh, effectPngs.Count);
        if (selected.Count == 0)
            return new LegacyWeaponEffectTint(
                effectPngs.ToList(), effectBlps?.ToList(), selected);

        var pngs = effectPngs.ToList();
        var blps = effectBlps?.ToList();
        foreach (int textureSlot in selected)
        {
            int index = textureSlot - 1;
            if (pngs[index] is not { Length: > 0 } sourcePng)
                throw new InvalidOperationException(
                    $"Effect texture slot {textureSlot} has no decoded PNG source.");

            byte[]? tinted = NativeWeaponEffectRecolor.TintPng(
                sourcePng, targetHueDegrees, targetSaturation);
            if (tinted is not { Length: > 0 })
                throw new InvalidOperationException(
                    $"Effect texture slot {textureSlot} could not be hue-shifted safely.");

            pngs[index] = tinted;
            // CustomWeaponBuildService prefers a non-empty source BLP over the PNG. Empty only the
            // changed slot so it is re-encoded; untouched effect slots remain exact source BLPs.
            if (blps is not null) blps[index] = Array.Empty<byte>();
        }

        return new LegacyWeaponEffectTint(pngs, blps, selected);
    }
}
