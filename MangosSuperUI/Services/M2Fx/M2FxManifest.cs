using System.Text.Json.Nodes;

namespace MangosSuperUI.Services.M2Fx;

/// <summary>
/// The time-varying MATERIAL state of an M2, in a form a browser can replay — the channel that was
/// missing between the exporter and the viewer.
///
/// === Why this exists ===
///
/// glTF can animate node transforms and skins. It cannot animate a material's colour, opacity or UV
/// transform without KHR_animation_pointer, which neither SharpGLTF 1.0.6 nor three.js's stock
/// GLTFLoader speaks. So every writer here sampled those tracks once at rest and baked the result
/// into a constant, and every glow, pulse, shimmer and scroll in the game's art arrived in the
/// previewer as a still frame. The forge's own <c>M2GlowPulseWriter</c> is the sharpest case: it
/// authors a floor→1.0→floor alpha pulse on a global sequence and ships it in the patch, and the
/// previewer re-read that model and rendered it frozen at key 0 — the DIMMEST point of the pulse.
///
/// The data has to travel some other way, and glTF has exactly one such lane: <c>extras</c>, a free
/// JSON blob the spec requires loaders to preserve. The codebase believed it was unavailable
/// ("SharpGLTF.Toolkit 1.0.6 doesn't expose a portable way to set them" — the reason geoset metadata
/// is smuggled through mesh NAMES), which is why nothing wider than a name suffix was ever built.
/// That is not true of this version: extras round-trip through ModelRoot, Mesh and Material and
/// arrive in three.js as <c>userData</c>. A name suffix can carry a number; extras can carry
/// keyframes, which is what a pulse actually is.
///
/// === Shape of the wire format ===
///
/// One manifest per GLB, on the root, under <c>suiFx</c>. Meshes are keyed by their glTF name, which
/// both writers already make unique and stable per submesh. Values are normalised for the client:
/// colours and alphas 0–1, times in milliseconds, angles in radians.
///
/// <code>
/// {
///   "v": 1,
///   "loops": [1800, 6667],                  // global-sequence durations, ms, by index
///   "meshes": {
///     "Geoset3": {
///       "rgb":   { "dur": 1800, "t": [0,900,1800], "v": [[1,1,1],[1,.55,.1],[1,1,1]] },
///       "alpha": { "dur": 1800, "t": [0,900,1800], "v": [0.55,1,0.55] },
///       "uv":    { "translate": { "dur": 4000, "t": [...], "v": [[u,v,0],...] } }
///     }
///   }
/// }
/// </code>
///
/// A mesh with nothing animated is omitted entirely, and a model with no animated mesh writes no
/// manifest at all — so a GLB that has nothing to say is byte-identical to what this build produced
/// before the manifest existed.
/// </summary>
public sealed record M2FxManifest(
    IReadOnlyList<uint> GlobalSequenceMs,
    IReadOnlyDictionary<string, M2FxMesh> Meshes,
    IReadOnlyList<M2FxEmitter> Emitters)
{
    public const int Version = 1;

    /// <summary>Root-extras property name. Also the key the client reads.</summary>
    public const string ExtrasKey = "suiFx";

    public bool Any => Meshes.Count > 0 || Emitters.Count > 0;

    /// <summary>The value to assign to a glTF object's <c>Extras</c>.
    ///
    /// Namespaced under <see cref="ExtrasKey"/> rather than written at the top level: extras is a
    /// shared bag that any tool in a glTF pipeline may add to, and a manifest that claimed bare
    /// property names like "meshes" would collide with the next thing that wants one.</summary>
    public JsonNode ToExtras() => new JsonObject { [ExtrasKey] = ToJson() };

    public JsonNode ToJson()
    {
        var meshes = new JsonObject();
        foreach (var (name, mesh) in Meshes) meshes[name] = mesh.ToJson();

        var loops = new JsonArray();
        foreach (uint ms in GlobalSequenceMs) loops.Add(ms);

        var o = new JsonObject
        {
            ["v"] = Version,
            ["loops"] = loops,
            ["meshes"] = meshes,
        };

        if (Emitters.Count > 0)
        {
            var emitters = new JsonArray();
            foreach (var e in Emitters) emitters.Add(e.ToJson());
            o["emitters"] = emitters;
        }
        return o;
    }
}

/// <summary>
/// One particle emitter, in enough detail for a browser to re-simulate it.
///
/// === Why the whole record travels, not a summary ===
///
/// A particle effect is not a picture, it is a rate. The forge learned this the expensive way on
/// Worldbreaker (see ARMOR_FORGE.md §8c): position, colour and size were all correct and the effect
/// still read as a strobe, because the two numbers that decide whether particles OVERLAP — rate and
/// lifespan — had been dropped. The previewer can only avoid repeating that by receiving the same
/// numbers the client gets.
///
/// Everything is in the GLB's coordinate space and the client's units: positions Y-up (M2Reader has
/// already converted them), colours 0–1, times in seconds, angles in radians.
///
/// <paramref name="Texture"/> is an index into the glTF's own texture array, not the M2's. The sheet
/// is embedded in the GLB's binary chunk as a normal glTF image that no material references — which
/// SharpGLTF preserves — so the client resolves it with the loader it already has instead of needing
/// a second request or a base64 blob in the JSON.
/// </summary>
public sealed record M2FxEmitter(
    float[] Position,
    int Texture,
    int BlendMode,
    int EmitterType,
    float EmissionRate,
    float Lifespan,
    float Speed,
    float SpeedVariation,
    float VerticalRange,
    float HorizontalRange,
    float Gravity,
    float AreaLength,
    float AreaWidth,
    float ZSource,
    float Midpoint,
    float[] ScaleRamp,
    float[][] ColorRamp,
    float[] AlphaRamp,
    int[] HeadCells,
    int[] TailCells,
    int TileRows,
    int TileCols)
{
    /// <summary>Particles alive in the steady state — what the client budgets its buffer from.</summary>
    public float SteadyStateParticles => EmissionRate * Lifespan;

    public JsonNode ToJson()
    {
        var colors = new JsonArray();
        foreach (var c in ColorRamp) colors.Add(new JsonArray(R(c[0]), R(c[1]), R(c[2])));

        return new JsonObject
        {
            ["pos"] = new JsonArray(R(Position[0]), R(Position[1]), R(Position[2])),
            ["tex"] = Texture,
            ["blend"] = BlendMode,
            ["type"] = EmitterType,
            ["rate"] = R(EmissionRate),
            ["life"] = R(Lifespan),
            ["speed"] = R(Speed),
            ["speedVar"] = R(SpeedVariation),
            ["vRange"] = R(VerticalRange),
            ["hRange"] = R(HorizontalRange),
            ["gravity"] = R(Gravity),
            ["areaL"] = R(AreaLength),
            ["areaW"] = R(AreaWidth),
            ["zSource"] = R(ZSource),
            ["mid"] = R(Midpoint),
            ["scale"] = new JsonArray(R(ScaleRamp[0]), R(ScaleRamp[1]), R(ScaleRamp[2])),
            ["color"] = colors,
            ["alpha"] = new JsonArray(R(AlphaRamp[0]), R(AlphaRamp[1]), R(AlphaRamp[2])),
            ["head"] = new JsonArray(HeadCells[0], HeadCells[1], HeadCells[2]),
            ["tail"] = new JsonArray(TailCells[0], TailCells[1], TailCells[2]),
            ["rows"] = TileRows,
            ["cols"] = TileCols,
        };
    }

    private static float R(float f) => float.IsFinite(f) ? MathF.Round(f, 5) : 0f;
}

/// <summary>
/// Everything animated about one mesh's material, plus the rest value of each channel it does NOT
/// animate.
///
/// Every track here is ABSOLUTE for its channel, never a multiplier on what the exporter baked. The
/// baked values are one sample of these same curves, so treating a track as a modulation of the
/// bake would apply that sample twice — a pulse floor of 0.55 would render at 0.30. Carrying the
/// rest values alongside lets the client compute the channel outright:
///
///   colour  = rgb   ?? baseRgb
///   opacity = (alpha ?? baseAlpha) × (weight ?? baseWeight)
///
/// and ignore <c>baseColorFactor</c> entirely for a mesh that appears here. Meshes absent from the
/// manifest keep rendering from the bake exactly as before, so a client that never loads this is
/// unchanged rather than broken.
/// </summary>
public sealed record M2FxMesh(
    M2FxTrack? Rgb,
    M2FxTrack? Alpha,
    M2FxTrack? Weight,
    M2FxUv? Uv,
    float[]? BaseRgb = null,
    float? BaseAlpha = null,
    float? BaseWeight = null)
{
    public bool Any => Rgb is not null || Alpha is not null || Weight is not null || Uv is { Any: true };

    public JsonNode ToJson()
    {
        var o = new JsonObject();
        if (Rgb is not null) o["rgb"] = Rgb.ToJson();
        if (Alpha is not null) o["alpha"] = Alpha.ToJson();
        if (Weight is not null) o["weight"] = Weight.ToJson();
        if (Uv is { Any: true }) o["uv"] = Uv.ToJson();
        if (BaseRgb is { Length: 3 } c) o["baseRgb"] = new JsonArray(c[0], c[1], c[2]);
        if (BaseAlpha is { } a) o["baseAlpha"] = a;
        if (BaseWeight is { } w) o["baseWeight"] = w;
        return o;
    }
}

/// <summary>
/// The UV texture transform. <paramref name="Base"/> is the rest value the exporter did NOT bake
/// into the vertices, as [translateU, translateV, rotateRadians, scaleU, scaleV]; the client
/// composes it about (0.5, 0.5) exactly as the client does.
/// </summary>
public sealed record M2FxUv(
    float[]? Base,
    M2FxTrack? Translate,
    M2FxTrack? Rotate,
    M2FxTrack? Scale)
{
    public bool Any => Translate is not null || Rotate is not null || Scale is not null;

    public JsonNode ToJson()
    {
        var o = new JsonObject();
        if (Base is { Length: 5 })
            o["base"] = new JsonArray(Base[0], Base[1], Base[2], Base[3], Base[4]);
        if (Translate is not null) o["translate"] = Translate.ToJson();
        if (Rotate is not null) o["rotate"] = Rotate.ToJson();
        if (Scale is not null) o["scale"] = Scale.ToJson();
        return o;
    }
}

/// <summary>
/// One animated channel: timestamps in ms against a loop of <paramref name="DurationMs"/>, and one
/// key per timestamp of <paramref name="Components"/> floats.
///
/// <paramref name="Step"/> is true for interpolation type 0, where a key holds until the next one
/// rather than blending — getting this wrong turns a blink into a fade and a fade into a blink.
/// Hermite/Bezier tracks are narrowed to linear over their value component; the client has no
/// tangent evaluator and the visual difference on a colour pulse is not worth one.
/// </summary>
public sealed record M2FxTrack(uint DurationMs, int Components, bool Step, uint[] Times, float[][] Keys)
{
    public JsonNode ToJson()
    {
        var t = new JsonArray();
        foreach (uint ms in Times) t.Add(ms);

        var v = new JsonArray();
        foreach (var key in Keys)
        {
            if (Components == 1) { v.Add(Round(key[0])); continue; }
            var comp = new JsonArray();
            for (int i = 0; i < Components && i < key.Length; i++) comp.Add(Round(key[i]));
            v.Add(comp);
        }

        var o = new JsonObject { ["dur"] = DurationMs, ["t"] = t, ["v"] = v };
        if (Step) o["step"] = true;
        return o;
    }

    // Five decimals is well past what an 8-bit colour channel or a UV offset can show, and it keeps
    // the manifest small enough to stay noise in the GLB's JSON chunk.
    private static float Round(float f) => float.IsFinite(f) ? MathF.Round(f, 5) : 0f;
}
