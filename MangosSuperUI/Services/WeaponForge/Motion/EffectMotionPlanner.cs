using System.Numerics;
using MangosSuperUI.Services.WeaponForge.RawM2;

namespace MangosSuperUI.Services.WeaponForge.Motion;

/// <summary>
/// Turns a later-client model's particle effects into a plan for rebuilding them with 1.12's own
/// moving parts — the "derive the intent, then fudge it with something that actually moves" step.
///
/// The forge used to bake every source emitter into a static additive sprite. That is geometrically
/// faithful and visually wrong: a still picture of fire reads as a decal, not as fire. 1.12 is not
/// short of motion — it ships 391 working item emitters of its own — so the fix is to say what the
/// source effect WAS and rebuild it, rather than photograph it.
///
/// Intent comes from three things the source hands over, in descending order of reliability:
///   1. the emitter's <b>texture</b> — 83% of TBC / 66% of WotLK emitter textures are ones 1.12 also
///      ships on an item, which makes the rebuild exact rather than approximate;
///   2. the emitter's <b>colour</b> — carried across verbatim, so a green fel flame stays green;
///   3. the texture's <b>name</b>, when 1.12 has no such texture — artists name their sheets for what
///      they are ("EMBER", "ICEGLOWBALL", "T_VFX_FIRE_ANIM02"), which is the most reliable statement
///      of intent available offline.
///
/// Everything this produces is an invented conversion and is logged as one.
/// </summary>
public static class EffectMotionPlanner
{
    /// <summary>Cap per model. Vanilla item models run 1–5 emitters; a WotLK shoulder can declare a
    /// dozen, and transplanting all of them would both bloat the M2 and swamp the silhouette.</summary>
    public const int MaxEmittersPerModel = 5;

    public sealed record Plan(
        IReadOnlyList<M2EmitterTransplanter.Graft> Grafts,
        IReadOnlyList<string> Notes,
        int SourceEmitterCount)
    {
        public bool Any => Grafts.Count > 0;
    }

    /// <param name="sourceEmitters">Source emitters, positions already in the FINAL model's WoW space
    /// (i.e. after any placement transform the owner applied).</param>
    /// <param name="readVanilla">Reader for stock 1.12 members — the donors live there.</param>
    /// <param name="packagedTextureFor">Source texture stem → the MPQ member this build packages for
    /// it, when the import carried that texture across. Null result = use the donor's stock texture.</param>
    public static Plan Build(
        IReadOnlyList<M2ParticleEmitterInfo> sourceEmitters,
        IReadOnlyList<Vector3> positionsWoW,
        Func<string, byte[]?> readVanilla,
        Func<string, string?>? packagedTextureFor,
        string label)
    {
        var grafts = new List<M2EmitterTransplanter.Graft>();
        var notes = new List<string>();
        if (sourceEmitters.Count == 0) return new Plan(grafts, notes, 0);

        var donorCache = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        byte[]? Load(string path)
        {
            if (donorCache.TryGetValue(path, out var cached)) return cached;
            byte[]? bytes = null;
            try { bytes = readVanilla(path); } catch { /* a missing donor is a downgrade, not a failure */ }
            donorCache[path] = bytes;
            return bytes;
        }

        // Biggest particles first: if the cap bites, keep the ones that carry the look.
        var order = Enumerable.Range(0, sourceEmitters.Count)
            .OrderByDescending(i => sourceEmitters[i].Scale)
            .ToList();

        int taken = 0, sizeless = 0;
        foreach (int i in order)
        {
            if (taken >= MaxEmittersPerModel) break;
            var e = sourceEmitters[i];
            // A source particle with no size draws nothing in the source either. Rebuilding it would
            // inherit the DONOR's size and paint a blob the original never had — measured on
            // Worldbreaker's headpiece, whose three emitters include two at scale 0.000.
            if (!(e.Scale > 0.001f)) { sizeless++; continue; }
            string stem = e.TextureName is null ? "" : Path.GetFileNameWithoutExtension(e.TextureName);

            var exact = VanillaEmitterDonors.ByTexture(stem);
            var donor = exact ?? PickByIntent(stem, e.ColorRgb, e.TileRows * e.TileCols);
            var donorBytes = Load(donor.ModelPath);
            if (donorBytes is null)
            {
                // Fall back to the most universally present donor before giving up on this emitter.
                var fallback = VanillaEmitterDonors.Catalog[0];
                donorBytes = Load(fallback.ModelPath);
                if (donorBytes is null) { notes.Add($"{label}: no stock donor readable for '{stem}' — left static."); continue; }
                donor = fallback;
                exact = null;
            }
            if (!M2EmitterTransplanter.IsTransplantable(donorBytes, donor.EmitterIndex, out string why))
            {
                notes.Add($"{label}: donor {Path.GetFileName(donor.ModelPath)} unusable ({why}) — '{stem}' left static.");
                continue;
            }

            // Texture: the source's own sheet when this build packaged it, else the donor's stock one
            // — read out of the DONOR MODEL rather than trusting the catalog's hand-written path.
            // Those paths were wrong for 20 of 27 entries (they assumed every sheet lives beside its
            // model; most live under SPELLS\, CREATURE\ or WORLD\), and naming a member the client
            // cannot open yields an emitter with no texture, which draws nothing and looks exactly
            // like an effect that was never grafted at all.
            string? packaged = stem.Length > 0 ? packagedTextureFor?.Invoke(stem) : null;
            string? donorTexture = M2EmitterTransplanter.ResolveDonorTexture(donorBytes, donor.EmitterIndex);
            if (donorTexture is null)
                notes.Add($"{label}: could not read {Path.GetFileNameWithoutExtension(donor.ModelPath)}'s own texture " +
                          $"table — falling back to the catalog path '{donor.TexturePath}'.");
            string texturePath = packaged ?? donorTexture ?? donor.TexturePath;

            var pos = i < positionsWoW.Count ? positionsWoW[i] : Vector3.Zero;
            VanillaEmitterDonors.ShapeFromName(stem, out bool namedIntent);
            string how = exact is not null
                ? $"1.12 ships {stem} on its own items — rebuilt exactly"
                : $"no 1.12 {stem}; read as {donor.Shape.ToString().ToLowerInvariant()} from its "
                  + (namedIntent ? "name" : "colour")
                  + $" and rebuilt from {Path.GetFileNameWithoutExtension(donor.ModelPath)}"
                  + (packaged is not null ? ", keeping the source texture" : "");

            grafts.Add(new M2EmitterTransplanter.Graft(
                DonorM2: donorBytes,
                DonorEmitterIndex: donor.EmitterIndex,
                PositionWoW: pos,
                ColorRgb: e.ColorRgb,
                Scale: e.Scale > 0.0001f ? e.Scale : null,
                TexturePath: texturePath,
                Describe: stem.Length > 0 ? stem : "emitter",
                Motion: e.Motion,
                ColorRamp: e.ColorRamp));
            notes.Add($"{label}: {stem} → {how}" +
                      (e.ColorRgb is { } c ? $", colour ({(int)c.X},{(int)c.Y},{(int)c.Z})" : "") +
                      $", scale {e.Scale:F3}" +
                      (e.Motion is { } mo
                          ? $", timing carried across ({mo.EmissionRate:F1}/s × {mo.Lifespan:F2}s ≈ {mo.SteadyStateParticles:F0} particles)"
                          : ", source timing unreadable — donor's kept") +
                      ".");
            taken++;
        }

        if (sizeless > 0)
            notes.Add($"{label}: {sizeless} source emitter(s) skipped — zero particle size, they draw nothing in the source either.");
        if (sourceEmitters.Count - sizeless > taken)
            notes.Add($"{label}: {sourceEmitters.Count - sizeless - taken} further source emitter(s) not rebuilt (cap {MaxEmittersPerModel}, largest kept).");
        return new Plan(grafts, notes, sourceEmitters.Count);
    }

    /// <summary>No 1.12 texture of that name: decide what the effect is MEANT to be.
    ///
    /// The name goes first — artists label their sheets ("EMBER", "ICEGLOWBALL", "T_VFX_FIRE_ANIM02").
    /// When it says nothing, which is common for the per-item sheets the late sets use
    /// (<c>SHOULDER_MAIL_RAIDHUNTER_G_01_PARTICLE</c>), the <b>colour</b> decides instead, and it is a
    /// strong signal: Worldbreaker's shoulder and helm particles are (255,121,23), which is fire and
    /// nothing else. Defaulting those to a glow ball is how the rebuild ends up looking wrong even
    /// though the machinery worked.
    ///
    /// <paramref name="sourceTiles"/> is the source emitter's texture-sheet cell count (rows × cols).
    /// It picks between donors of the SAME shape, because the donor keeps its own texture and
    /// therefore its own tile grid: a source that is one still image (1×1 — the common case for the
    /// per-item <c>*_PARTICLE</c> sheets) reads badly on a donor whose sprite flicks through a 4×4
    /// flipbook, and a source that IS a flipbook looks dead on a donor that never advances a cell.
    /// Measured: Worldbreaker's shoulder is 1×1 and the first Flame donor in the catalog is
    /// FLAMELICKSMALL at 4×4, so the shape was right and the cadence was not.</summary>
    private static VanillaEmitterDonors.Donor PickByIntent(string stem, Vector3? colour, int sourceTiles)
    {
        var shape = VanillaEmitterDonors.ShapeFromName(stem, out bool named);
        var hue = colour is { } c ? ItemVisualSuggester.HueOf(c) : ItemVisualSuggester.Hue.None;

        if (named)
        {
            // A "flame" in an unmistakably cold colour is frost dressed as fire; a frost sparkle
            // reads better than an orange lick tinted blue.
            if (shape == VanillaEmitterDonors.EffectShape.Flame &&
                hue is ItemVisualSuggester.Hue.Blue or ItemVisualSuggester.Hue.Purple)
                return VanillaEmitterDonors.Best(VanillaEmitterDonors.EffectShape.Star, sourceTiles);
            return VanillaEmitterDonors.Best(shape, sourceTiles);
        }

        // Name said nothing — go on colour.
        var fromHue = hue switch
        {
            ItemVisualSuggester.Hue.Red or ItemVisualSuggester.Hue.Yellow => VanillaEmitterDonors.EffectShape.Flame,
            ItemVisualSuggester.Hue.Green => VanillaEmitterDonors.EffectShape.Flame,   // fel fire
            ItemVisualSuggester.Hue.Blue or ItemVisualSuggester.Hue.Purple => VanillaEmitterDonors.EffectShape.Star,
            ItemVisualSuggester.Hue.White => VanillaEmitterDonors.EffectShape.Star,
            ItemVisualSuggester.Hue.Black => VanillaEmitterDonors.EffectShape.Smoke,
            _ => VanillaEmitterDonors.EffectShape.Glow,
        };
        return VanillaEmitterDonors.Best(fromHue, sourceTiles);
    }
}
