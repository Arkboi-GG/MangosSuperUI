using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace MangosSuperUI.Services;

using VERTEX = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

/// <summary>
/// Converts a parsed M2Model + BLP textures into a GLB (glTF Binary) file.
/// Uses SharpGLTF Toolkit (MeshBuilder + SceneBuilder) API.
///
/// Ported from MangosSuperUI_Extractor.GlbWriter — uses SkiaSharp instead
/// of System.Drawing for Linux (server-side) compatibility.
///
/// Each submesh becomes a separate mesh in the scene (like wow.export's "Geoset0", "Geoset1")
/// with its own material/texture. This prevents SharpGLTF from merging primitives that
/// share the same material.
///
/// Triangle winding: M2 indices are emitted in native order (i0, i1, i2) — no swap needed.
/// The Z-up → Y-up coordinate transform in M2Reader already flips handedness, so the
/// indices come out in glTF's expected counter-clockwise front-face convention as-is.
///
/// === Session M, then Session M-revert ===
/// Session M added "weapon mount-offset baking" — negating the weapon
/// M2's Attachment-0 position into the scene root translation, on the
/// theory that Attachment-0 in an item M2 is the hilt mount point.
///

/// The Attachment struct owns exactly one
/// (bone, position) pair (sourced from the CHARACTER M2's attachment
/// record) and weapons render with their vertices as-authored — the
/// M2 artist already placed the hilt at the model origin. The
/// weapon's own attachments are reserved for spell-visual EFFECT
/// mount-points (glow on enchanted weapons, Effect class with
/// itemVisualEffectId), not for geometry positioning.
///
/// Empirical confirmation of the offset error: for displayId 1542
/// (Sword_1H_Short_A_02.mdx), Session M placed the weapon mesh at
/// world (-0.285, 0.899, 0.476) while the hand bone was at world
/// (-0.059, 0.904, 0.476) — a (-0.226, -0.005, 0) push that turned
/// out to be exactly the M2's Attachment-0 position, the spot the
/// glow effect would mount, not the grip.
///
/// Revert behavior: scene root = Matrix4x4.Identity. The weapon's
/// vertex origin sits at whichever character bone the client mounts
/// it under (Attachment_1 = HandRight, Attachment_2 = HandLeft).
/// Visually awkward at character rest pose (no idle-pose rotation
/// on the hand → blade points along +X out of the hand instead of
/// down/back) but mechanically correct.
///
/// The "looks awkward" issue is a separate problem: vanilla WoW
/// applies an idle animation that rotates the hand bone so the
/// weapon sits naturally. Our character GLB ships in bind-pose with
/// identity rotations, so the hand is unrotated. Fixing it is an
/// idle-animation sampling problem, not a
/// GLB-writer problem.
///
/// === Cache impact ===
/// RigidGlbVersion bump invalidates the stale Session-M GLBs. The
/// CacheVersionRegistry sweep clears them; new requests regenerate
/// with identity transform. No code-side action beyond the version
/// bump.
/// </summary>
public static class GlbWriter
{
    /// <summary>
    /// Threshold below which a submesh's static alpha is considered "near
    /// transparent" — used by the diagnostic endpoint to flag candidate
    /// submeshes for inspection. Session N initially planned to skip
    /// submeshes below this threshold but reverted that decision:we
    /// render these submeshes with the computed alpha (no skip), and
    /// dropping the geometry would also drop legitimately-faded effects
    /// like a 19%-alpha lightning halo that's supposed to be present.
    ///
    /// The actual visibility decision now lives in baseColorFactor.A,
    /// baked per-material at GLB write time. This constant is retained
    /// only for the diagnostic's "near-zero" flag and external callers
    /// that may want to do their own filtering.
    /// </summary>
    public const float SUBMESH_VISIBILITY_THRESHOLD = 0.01f;

    /// <summary>
    /// Convert a parsed M2 + textures into a GLB on disk.
    ///
    /// === doubleSided ===
    /// When true, every material in the output is marked double-sided
    /// (glTF KHR_materials.doubleSided = true → three.js renders both
    /// faces regardless of triangle winding).
    ///
    /// This is needed for armor attachment models (helms, shoulders) where
    /// vanilla M2 geometry includes single-sided thin features (spaulder
    /// hanging flaps, helm horns/wings, cloak panels) whose authored
    /// winding renders the WRONG side toward the camera after our
    /// Z-up→Y-up flip — backface culling then hides them entirely.
    /// Session L empirical evidence (LShoulder_Plate_RaidPaladin_A_01):
    /// the upper "wing" portion is double-sided in the source and
    /// renders fine, the lower flap is single-sided and disappears
    /// until doubleSided=true.
    ///
    /// Default false because weapons (Session D) and rigid item models
    /// already render correctly with backface culling, and double-sided
    /// pixels cost real GPU fragment work — only opt in when you know
    /// the M2 has problematic single-sided geometry. Attachments YES;
    /// weapons NO.
    /// </summary>
    /// <param name="visualEffects">Separate effect models this item's <c>ItemVisual</c> mounts on it —
    /// enchant glows, Thunderfury's lightning, a Warglaive's fire. None of that is in the item's own
    /// bytes, so without this an item can be decoded perfectly and still render dead. Resolve them
    /// with <see cref="M2Fx.ItemVisualEffects.Resolve"/>; their emitters are folded into this GLB's
    /// manifest at their mount points and their sheets embedded alongside.</param>
    public static bool SaveGlb(M2Model m2, Dictionary<int, byte[]> textures, string outputPath,
        bool doubleSided = false,
        IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? visualEffects = null)
    {
        if (!m2.IsValid) return false;

        try
        {
            // ── Decode all source textures to PNG bytes ONCE (Session M phase 2.5).
            // We previously built one MaterialBuilder per texIdx eagerly. With
            // per-batch blend modes we need (texIdx × blendMode) materials, so
            // defer material construction to the per-submesh loop and just cache
            // the decoded PNG up front.
            var pngByTexIdx = new Dictionary<int, byte[]>();
            foreach (var (texIdx, blpData) in textures)
            {
                var pngBytes = ConvertBlpToPngBytes(blpData);
                if (pngBytes != null) pngByTexIdx[texIdx] = pngBytes;
            }

            var fallbackMat = new MaterialBuilder("default")
                .WithUnlitShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.7f, 0.7f, 0.7f, 1f));
            if (doubleSided) fallbackMat.WithDoubleSide(true);

            // Material cache keyed by (texIdx, blendMode, alphaBucket). Two
            // batches with the same texture, blend mode, and (rounded) alpha
            // share the material; differing on any of the three gets a
            // distinct material with a distinct name so the client can decode
            // the suffix per-mesh.
            //
            // Why alphaBucket: M2 transparency tracks produce floats with
            // arbitrary precision (e.g. 0.190008..). We round to 1% steps
            // so the cache stays small and the material name suffix
            // (mat_5_blend2_a19) is human-readable. Resolution at 1%
            // is well below perceptual threshold for the cases we care
            // about (faded lightning quads at 19%, etc.).
            //
            // Why the tint is in the key: the M2Color record's RGB rest sample is baked into
            // baseColorFactor below, and two batches can agree on texture/blend/alpha while pointing
            // at different colour records (a weapon whose blade glows orange and whose rune glows
            // blue off one texture). Merging those would paint both the same.
            var matCache = new Dictionary<(int texIdx, int blendMode, int alphaBucket, int tintBucket), MaterialBuilder>();

            MaterialBuilder GetMaterial(int texIdx, int blendMode, bool wantDoubleSide, float alpha = 1.0f,
                Vector3? tint = null)
            {
                // Clamp + bucket the alpha. 0 stays 0 (we'd skip the submesh
                // in some future world but right now we render it anyway —
                // see the per-submesh loop comments).
                if (alpha < 0f) alpha = 0f;
                if (alpha > 1f) alpha = 1f;
                int alphaBucket = (int)Math.Round(alpha * 100f);
                var rgb = tint ?? Vector3.One;
                if (!float.IsFinite(rgb.X) || !float.IsFinite(rgb.Y) || !float.IsFinite(rgb.Z)) rgb = Vector3.One;
                rgb = Vector3.Clamp(rgb, Vector3.Zero, Vector3.One);
                int tintBucket = ((int)MathF.Round(rgb.X * 255f) << 16)
                               | ((int)MathF.Round(rgb.Y * 255f) << 8)
                               | (int)MathF.Round(rgb.Z * 255f);

                var key = (texIdx, blendMode, alphaBucket, tintBucket);
                if (matCache.TryGetValue(key, out var existing)) return existing;

                // Three-tier resolution (matches pre-Session-M behavior):
                //   1. Exact texture match for this submesh's texIdx
                //   2. First-available texture (the common case for weapons —
                //      one texture loaded, many submeshes referencing
                //      texIdx values that don't directly index into it).
                //      Prefer a type=2 (DBC-supplied "item object skin")
                //      slot over a type=0 (M2-embedded environment/reflect
                //      map) when both are present — picking the reflect
                //      map as the base color is the Might-helm/shoulder bug.
                //   3. Grey fallback (only if zero textures decoded at all)
                // Losing tier 2 is what made the v4-regenerated Thunderfury
                // come out fully grey: 11 submeshes all referenced texIdx
                // values that weren't present in pngByTexIdx (the only
                // entry was at slot 0, but the batches were resolving to
                // other slots).
                byte[]? pngBytes = null;
                int resolvedTexIdx = texIdx;
                if (pngByTexIdx.TryGetValue(texIdx, out var exact))
                {
                    pngBytes = exact;
                }
                else if (pngByTexIdx.Count > 0)
                {
                    // Prefer the lowest-index type=2 slot (the DBC-supplied
                    // diffuse) over any type=0 slot (embedded reflection
                    // maps like ShoulderReflect01.blp). If neither is
                    // present, fall back to dictionary-insertion order —
                    // single-texture weapons land here and there's nothing
                    // to disambiguate.
                    int? preferred = null;
                    foreach (var kvp in pngByTexIdx)
                    {
                        if (kvp.Key < m2.Textures.Count && m2.Textures[kvp.Key].Type == 2)
                        {
                            if (preferred == null || kvp.Key < preferred.Value)
                                preferred = kvp.Key;
                        }
                    }
                    if (preferred != null)
                    {
                        resolvedTexIdx = preferred.Value;
                        pngBytes = pngByTexIdx[preferred.Value];
                    }
                    else
                    {
                        var first = pngByTexIdx.First();
                        resolvedTexIdx = first.Key;
                        pngBytes = first.Value;
                    }
                }

                if (pngBytes == null) return fallbackMat;

                var img = new SharpGLTF.Memory.MemoryImage(pngBytes);
                // Name suffix _blendN tells the client to set three.js blending
                // accordingly. See character-viewer/blend-suffix.js applyBlendSuffix.
                // We append _a{NN} (1% steps) when alpha < 1 so the client could
                // also decode the alpha factor if needed; SharpGLTF writes the
                // factor into pbrMetallicRoughness.baseColorFactor[3] regardless
                // so three.js already sees the correct alpha at the standard
                // glTF level — the name suffix is purely diagnostic.
                var alphaSuffix = alphaBucket < 100 ? $"_a{alphaBucket:D2}" : "";
                var name = $"mat_{resolvedTexIdx}_blend{blendMode}{alphaSuffix}";

                // Session N: bake static alpha into baseColorFactor.
                //
                //   WithBaseColor(img) sets the texture and an implicit
                //   RGBA factor of (1,1,1,1). To override the factor we
                //   call WithChannelParam after — same pattern used by
                //   SharpGLTF's own SceneBuilderTests/Example1. glTF's
                //   baseColorFactor[3] is the canonical place to put an
                //   overall material alpha multiplier; three.js reads it
                //   into Material.color.a automatically.
                //
                // The RGB half is the M2Color record's rest tint, which used to be dropped on the
                // floor — GlbWriter never read ReachableRestColors at all. That is why an authored
                // coloured glow (a fel-green rune, an orange blade) arrived in the previewer white:
                // the geometry and the additive blend were right and the colour the artist put on
                // the pass was simply not exported. Models with no colour record pass Vector3.One
                // and are unchanged.
                var mat = new MaterialBuilder(name)
                    .WithUnlitShader()
                    .WithBaseColor(img)
                    .WithChannelParam(
                        KnownChannel.BaseColor,
                        KnownProperty.RGBA,
                        new Vector4(rgb, alpha));

                // M2 blend modes (vanilla):
                //   0 = opaque         (default, no alpha)
                //   1 = alpha-key       (cutout — alphaTest)
                //   2 = alpha-blend     (standard transparency)
                //   3 = additive        (glow, additive blending)
                //   4 = add-alpha       (additive with alpha modulation)
                //   5 = modulate        (multiply)
                //   6 = mod2x           (multiply by 2x, rare)
                // For glTF we can only signal "this material is transparent or
                // opaque" — three.js then reads the suffix and applies the
                // specific blend equation.
                //
                // Session N: alpha < 1 forces BLEND even when blendMode is 0,
                // because an opaque material with a baseColorFactor.A < 1
                // gets ignored under AlphaMode.OPAQUE — alpha only counts
                // under MASK or BLEND. This is what lets the Thunderfury
                // lightning quads (alpha 0.19, blendMode 5 modulate) fade
                // away instead of rendering as flat opaque billboards.
                if (blendMode >= 2 || alphaBucket < 100)
                {
                    mat.WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND);
                }
                else if (blendMode == 1)
                {
                    mat.WithAlpha(SharpGLTF.Materials.AlphaMode.MASK, 0.5f);
                }
                if (wantDoubleSide) mat.WithDoubleSide(true);

                // Cache under the FULL key (caller's texIdx, blendMode,
                // alphaBucket). A future call with the same triple gets
                // the same MaterialBuilder, even though internally we
                // resolved to a different texture for tier-2 fallback.
                matCache[key] = mat;
                return mat;
            }

            // Session M-revert: scene root is identity (mount-offset baking was
            // wrong — see class docstring).
            var rootMatrix = Matrix4x4.Identity;

            var scene = new SceneBuilder("scene");
            var vertices = m2.Vertices;
            var indices = m2.Indices;

            // Build a per-submesh blend mode lookup. Resolved via the M2 batch
            // chain: batch.SubmeshIndex → batch.MaterialIndex → m2.RenderFlags[idx]
            // → blendingMode. Submeshes not referenced by any batch (rare)
            // default to opaque (0).
            var submeshBlend = BuildSubmeshBlendMap(m2);

            // ── Submesh visibility (Session N) ──
            // For each submesh, resolve the static alpha its first-listed
            // batch produces via the M2's transparency tracks:
            //   batch.TextureWeightIndex → TransparencyLookup[idx]
            //                            → TransparencyStaticAlphas[idx]
            //
            // Result gets baked into the GLB material's baseColorFactor.A
            // and the material is flagged AlphaMode.BLEND when below 1.0.
            // Three.js reads the factor automatically; no client-side
            // changes needed.
            //
            // This is what makes Thunderfury's lightning quad geosets
            // (alpha 0.19 in their authored idle pose) render as faint
            // tints instead of flat opaque blue billboards. Hilt + blade
            // come back at 1.0 and render normally.
            //
            // We intentionally do NOT drop low-alpha submeshes — we
            // render them with their computed alpha, and skipping them
            // would discard legitimately-faded effects that the M2 author
            // wanted visible-but-subtle in the default pose.
            var submeshVis = BuildSubmeshVisibilityMap(m2);
            var submeshTint = BuildSubmeshTintMap(m2);

            // Submesh index → the glTF mesh name it was written under. The animation manifest keys
            // on these, so it has to be recorded as the meshes are built rather than reconstructed.
            var meshNameForSubmesh = new Dictionary<int, string>();

            if (m2.Submeshes.Count > 1)
            {
                // ── Multi-submesh: build a SEPARATE MeshBuilder per submesh ──
                var submeshTexture = BuildSubmeshTextureMap(m2);

                for (int subIdx = 0; subIdx < m2.Submeshes.Count; subIdx++)
                {
                    var submesh = m2.Submeshes[subIdx];
                    if (submesh.IndexCount == 0 || submesh.IndexCount % 3 != 0) continue;

                    // Session N: per-batch static alpha from the M2's
                    // transparency tracks. Submeshes 0-6 of Thunderfury
                    // (the lightning fins) come back with alpha ~0.19 here;
                    // hilt + blade come back 1.0. The alpha gets baked into
                    // the GLB material's baseColor.A and the material is
                    // flagged AlphaMode.BLEND when below 1, so the renderer
                    // applies it instead of treating the geometry as opaque.
                    //
                    // We do NOT skip the submesh even when alpha is low —
                    // we render it with the computed alpha (which is then multiplied by the blend
                    // mode behavior). Dropping the geometry would also drop
                    // the "barely-visible faded lightning halo" that's
                    // supposed to be there in the static pose.
                    float vis = submeshVis.ContainsKey(subIdx) ? submeshVis[subIdx] : 1.0f;

                    int texIdx = submeshTexture.ContainsKey(subIdx) ? submeshTexture[subIdx] : subIdx;
                    int blendMode = submeshBlend.ContainsKey(subIdx) ? submeshBlend[subIdx] : 0;
                    var tint = submeshTint.TryGetValue(subIdx, out var t) ? t : (Vector3?)null;
                    var mat = GetMaterial(texIdx, blendMode, doubleSided, vis, tint);

                    string meshName = $"Geoset{subIdx}";
                    meshNameForSubmesh[subIdx] = meshName;
                    var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(meshName);
                    var prim = meshBuilder.UsePrimitive(mat);

                    for (int i = submesh.IndexStart; i + 2 < submesh.IndexStart + submesh.IndexCount; i += 3)
                    {
                        if (i + 2 >= indices.Count) break;
                        int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                        if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count) continue;
                        prim.AddTriangle(MakeVertex(vertices[i0]), MakeVertex(vertices[i1]), MakeVertex(vertices[i2]));
                    }

                    scene.AddRigidMesh(meshBuilder, rootMatrix);
                }
            }
            else
            {
                // ── Single submesh or no submesh info: one mesh ──
                // Blend lookup still applies: a single-submesh M2 may carry an
                // additive material (rare for weapons, common for spell M2s).
                int singleBlend = submeshBlend.ContainsKey(0) ? submeshBlend[0] : 0;
                float singleVis = submeshVis.ContainsKey(0) ? submeshVis[0] : 1.0f;

                // Texture selection MUST follow the same batch chain the
                // multi-submesh branch uses:
                //   batch[0].SubmeshIndex(=0)
                //     → batch[0].TextureIndex
                //     → TextureLookup[ ... ]
                //     → texIdx into m2.Textures
                //
                // Why this matters even though there's only one submesh:
                // when an M2 has multiple textures (e.g. type=2 DBC diffuse
                // at slot 0 + type=0 embedded reflection at slot 1) and only
                // one submesh, `pngByTexIdx.Keys.First()` returns whichever
                // slot got inserted into the dictionary first — which is a
                // function of the texture-collection loop order in
                // ItemTextureService, NOT what the M2's batch actually wants
                // rendered as the diffuse.
                //
                // Empirical: Helm of Might (displayId 31260) and Pauldrons of
                // Might (31024) both have textureCount=2, submeshCount=1, and
                // both came out grey (the type=0 ShoulderReflect01.blp was
                // baked as the material) until this branch was rewritten to
                // consult the batch chain. The fully-correct sister items
                // (Helm/Pauldrons of Wrath) have multiple submeshes and went
                // through the multi-submesh branch, hiding the bug.
                var submeshTextureSingle = BuildSubmeshTextureMap(m2);
                int singleTexIdx = submeshTextureSingle.ContainsKey(0)
                    ? submeshTextureSingle[0]
                    : (pngByTexIdx.Count > 0 ? pngByTexIdx.Keys.First() : 0);
                var singleTint = submeshTint.TryGetValue(0, out var st) ? st : (Vector3?)null;
                var mat = pngByTexIdx.Count > 0
                    ? GetMaterial(singleTexIdx, singleBlend, doubleSided, singleVis, singleTint)
                    : fallbackMat;
                meshNameForSubmesh[0] = "mesh";
                var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>("mesh");
                var prim = meshBuilder.UsePrimitive(mat);

                if (m2.Submeshes.Count == 1)
                {
                    var sub = m2.Submeshes[0];
                    for (int i = sub.IndexStart; i + 2 < sub.IndexStart + sub.IndexCount; i += 3)
                    {
                        if (i + 2 >= indices.Count) break;
                        int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                        if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count) continue;
                        prim.AddTriangle(MakeVertex(vertices[i0]), MakeVertex(vertices[i1]), MakeVertex(vertices[i2]));
                    }
                }
                else
                {
                    for (int i = 0; i + 2 < indices.Count; i += 3)
                    {
                        int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                        if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count) continue;
                        prim.AddTriangle(MakeVertex(vertices[i0]), MakeVertex(vertices[i1]), MakeVertex(vertices[i2]));
                    }
                }

                scene.AddRigidMesh(meshBuilder, rootMatrix);
            }

            // ── Save ──
            var model = scene.ToGltf2();

            // ── Animation manifest (glTF extras) ──
            // glTF cannot animate a material, so the colour / alpha / UV-scroll tracks ride out as a
            // JSON blob on the root and character-viewer/m2fx.js replays them per frame. Best-effort
            // and additive: a model with nothing animated writes no extras and the GLB is what it
            // always was. See Services/M2Fx/M2FxManifest.cs.
            try
            {
                // Particle-emitter sheets have to reach the browser somehow, and they are usually NOT
                // bound to any batch, so the material loop above never emitted them. They go in as
                // ordinary glTF images that no material references — verified to survive SharpGLTF's
                // save and reload with their names intact — which puts them in the GLB's binary chunk
                // where the client's own loader can resolve them, instead of a base64 blob in the JSON
                // or a second request against an endpoint that does not exist.
                var emitterTextureIndex = EmbedEmitterTextures(model, m2, pngByTexIdx);

                var fx = M2Fx.M2FxReader.Build(m2.SourceBytes, m2,
                    subIdx => meshNameForSubmesh.TryGetValue(subIdx, out var n) ? n : null,
                    slot => emitterTextureIndex.TryGetValue(slot, out int gltfIndex) ? gltfIndex : null);

                var mounted = EmbedVisualEffects(model, visualEffects);
                if (mounted.Count > 0)
                    fx = fx with { Emitters = fx.Emitters.Concat(mounted).ToList() };

                // WotLK (v264) fallback. M2WotlkReader parses fine but leaves SourceBytes null on
                // purpose (its raw emitter/track reader is v256-only), so M2FxReader.Build above found
                // no emitters and the preview shipped with an empty suiFx — which is why WotLK armour
                // (Worldbreaker's shoulder fire) rendered dead while the game showed it. Route the
                // emitters the WotLK reader DID decode into the manifest, resolving each sheet by name
                // against the already-embedded textures. Gated on SourceBytes==null so vanilla/TBC,
                // which use the higher-fidelity binary path, are untouched.
                if (fx.Emitters.Count == 0 && m2.SourceBytes is null && m2.ParticleEmitters.Count > 0)
                {
                    var wotlk = M2Fx.M2FxReader.BuildEmittersFromModel(m2,
                        name => EmbedNamedEmitterTexture(model, m2, pngByTexIdx, name));
                    if (wotlk.Count > 0) fx = fx with { Emitters = fx.Emitters.Concat(wotlk).ToList() };
                }

                if (fx.Any) model.Extras = fx.ToExtras();
            }
            catch { /* a manifest is an enhancement; never fail a GLB over one */ }

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            model.SaveGLB(outputPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Embed each emitter's texture sheet as a glTF image, returning M2 texture slot → glTF texture
    /// index for the manifest to reference.
    ///
    /// Emitter sheets are already in <paramref name="pngByTexIdx"/> for free: every caller populates
    /// the texture dictionary by walking the M2's whole texture table and pulling each type-0
    /// filename out of the MPQ, and an emitter's sheet is exactly such an entry. It simply never got
    /// used, because nothing binds it to a batch and the material loop only builds materials for
    /// batches.
    ///
    /// A slot with no decoded PNG is left out of the map, which makes M2FxReader drop that emitter.
    /// That is deliberate: an untextured additive quad is a white blob, and a white blob on a weapon
    /// is worse than no effect at all.
    /// </summary>
    private static Dictionary<int, int> EmbedEmitterTextures(
        SharpGLTF.Schema2.ModelRoot model, M2Model m2, IReadOnlyDictionary<int, byte[]> pngByTexIdx)
    {
        var map = new Dictionary<int, int>();

        foreach (int slot in M2Fx.M2FxReader.EmitterTextureSlots(m2.SourceBytes))
        {
            if (map.ContainsKey(slot)) continue;
            if (!pngByTexIdx.TryGetValue(slot, out var png) || png is not { Length: > 0 }) continue;

            // An emitter's sheet is frequently the same image a batch already renders with — a
            // flaming sword's blade texture IS its ember sheet — and the material loop has already
            // embedded that one. Re-adding it would put a second copy of the same PNG in the binary
            // chunk, which on a 128x128 sheet is tens of kilobytes per model for nothing. Match on
            // content rather than on slot, because SceneBuilder does not expose which glTF texture
            // came from which M2 slot.
            var existing = model.LogicalTextures.FirstOrDefault(t => SameImage(t.PrimaryImage, png));
            if (existing is not null) { map[slot] = existing.LogicalIndex; continue; }

            var image = model.CreateImage();
            image.Content = new SharpGLTF.Memory.MemoryImage(png);
            image.Name = $"EmitterSheet_{slot}";

            var texture = model.UseTexture(image);
            texture.Name = image.Name;
            map[slot] = texture.LogicalIndex;
        }

        return map;
    }

    /// <summary>
    /// Embed a WotLK emitter's sheet by FILENAME and return its glTF texture index. The v264 lane
    /// (<see cref="M2Handlers.M2WotlkReader"/>) exposes each emitter's sheet as a path on
    /// <c>M2ParticleEmitterInfo.TextureName</c> rather than as a slot, so match the name against the M2
    /// texture table to find the PNG the caller already decoded, then embed it exactly as
    /// <see cref="EmbedEmitterTextures"/> does (deduped by content). Null when the sheet is not among the
    /// decoded textures, which makes <see cref="M2Fx.M2FxReader.BuildEmittersFromModel"/> drop that
    /// emitter — same rule as the slot path, since an untextured additive quad is a white blob.
    /// </summary>
    private static int? EmbedNamedEmitterTexture(SharpGLTF.Schema2.ModelRoot model, M2Model m2,
        IReadOnlyDictionary<int, byte[]> pngByTexIdx, string? textureName)
    {
        if (string.IsNullOrEmpty(textureName)) return null;
        string want = textureName.Replace('/', '\\');

        for (int i = 0; i < m2.Textures.Count; i++)
        {
            if (!string.Equals(m2.Textures[i].Filename?.Replace('/', '\\'), want, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!pngByTexIdx.TryGetValue(i, out var png) || png is not { Length: > 0 }) return null;

            var existing = model.LogicalTextures.FirstOrDefault(t => SameImage(t.PrimaryImage, png));
            if (existing is not null) return existing.LogicalIndex;

            var image = model.CreateImage();
            image.Content = new SharpGLTF.Memory.MemoryImage(png);
            image.Name = $"WotlkEmitterSheet_{i}";
            var texture = model.UseTexture(image);
            texture.Name = image.Name;
            return texture.LogicalIndex;
        }
        return null;
    }

    /// <summary>
    /// Embed each mounted effect model's sheets and decode its emitters onto the host.
    ///
    /// The effect model's GEOMETRY is deliberately not merged. Vanilla enchant effects are emitters
    /// hung off an essentially empty model — RedFlame_Low is 2.7 KB — so the emitters are the whole
    /// visual, and pulling their geometry in would mean reconciling a second material/skin set for no
    /// gain. Ribbon emitters are the exception and are not handled anywhere yet.
    /// </summary>
    internal static List<M2Fx.M2FxEmitter> EmbedVisualEffects(
        SharpGLTF.Schema2.ModelRoot model, IReadOnlyList<M2Fx.ItemVisualEffects.Effect>? effects)
    {
        var result = new List<M2Fx.M2FxEmitter>();
        if (effects is null || effects.Count == 0) return result;

        foreach (var effect in effects)
        {
            // Decode the effect's own textures, then embed the ones its emitters actually sample.
            var pngBySlot = new Dictionary<int, byte[]>();
            foreach (var (slot, blp) in effect.Textures)
            {
                var png = ConvertBlpToPngBytes(blp);
                if (png is { Length: > 0 }) pngBySlot[slot] = png;
            }

            var indexBySlot = new Dictionary<int, int>();
            foreach (int slot in M2Fx.M2FxReader.EmitterTextureSlots(effect.M2))
            {
                if (indexBySlot.ContainsKey(slot)) continue;
                if (!pngBySlot.TryGetValue(slot, out var png)) continue;

                var existing = model.LogicalTextures.FirstOrDefault(t => SameImage(t.PrimaryImage, png));
                if (existing is not null) { indexBySlot[slot] = existing.LogicalIndex; continue; }

                var image = model.CreateImage();
                image.Content = new SharpGLTF.Memory.MemoryImage(png);
                image.Name = $"VisualSheet_{Path.GetFileNameWithoutExtension(effect.ModelPath)}_{slot}";
                var texture = model.UseTexture(image);
                texture.Name = image.Name;
                indexBySlot[slot] = texture.LogicalIndex;
            }

            result.AddRange(M2Fx.M2FxReader.ReadMountedEmitters(
                effect.M2,
                slot => indexBySlot.TryGetValue(slot, out int i) ? i : null,
                effect.MountMesh));
        }

        return result;
    }

    /// <summary>Byte-identical image content? Encoding is deterministic here (both sides come out of
    /// the same ConvertBlpToPngBytes call for the same slot), so equality is exact rather than
    /// perceptual.</summary>
    private static bool SameImage(SharpGLTF.Schema2.Image? image, byte[] png)
    {
        if (image is null) return false;
        var content = image.Content.Content;
        return content.Length == png.Length && content.Span.SequenceEqual(png);
    }

    /// <summary>
    /// Build a mapping of submeshIndex → the M2Color record's rest RGB, via
    ///   batch.SubmeshIndex → batch.ColorIndex → m2.ReachableRestColors[idx].Rgb.
    ///
    /// This is the tint the M2 author put on the material pass, and it was previously discarded:
    /// nothing in this file ever read ReachableRestColors, so a pass authored as a coloured glow
    /// exported white and the previewer showed white. Only the ~1% of batches that reference a
    /// colour record are affected; everything else stays at (1,1,1) and renders identically.
    ///
    /// Values outside 0–1 are dropped rather than clamped here — an out-of-range tint means the
    /// chain resolved to something that is not a colour, and a wrong tint is worse than none.
    /// </summary>
    private static Dictionary<int, Vector3> BuildSubmeshTintMap(M2Model m2)
    {
        var map = new Dictionary<int, Vector3>();

        foreach (var batch in m2.Batches)
        {
            int subIdx = batch.SubmeshIndex;
            if (map.ContainsKey(subIdx)) continue;
            if (batch.ColorIndex < 0) continue;
            if (!m2.ReachableRestColors.TryGetValue(batch.ColorIndex, out var rest)) continue;

            var rgb = rest.Rgb;
            if (!float.IsFinite(rgb.X) || !float.IsFinite(rgb.Y) || !float.IsFinite(rgb.Z)) continue;
            if (rgb.X < 0f || rgb.Y < 0f || rgb.Z < 0f) continue;
            if (rgb.X > 1f || rgb.Y > 1f || rgb.Z > 1f) continue;

            map[subIdx] = rgb;
        }

        return map;
    }

    /// <summary>
    /// Build a mapping of submeshIndex → blendMode using the batch chain:
    ///   batch.SubmeshIndex → batch.MaterialIndex → m2.RenderFlags[idx] → blendingMode.
    /// First-wins on duplicates (one batch per submesh is the common case;
    /// when there are layered batches on the same submesh, the first-listed
    /// is the base material). Submeshes with no batch reference fall back to
    /// opaque (0) via the caller's ContainsKey check.
    /// </summary>
    private static Dictionary<int, int> BuildSubmeshBlendMap(M2Model m2)
    {
        var map = new Dictionary<int, int>();

        foreach (var batch in m2.Batches)
        {
            int subIdx = batch.SubmeshIndex;
            if (map.ContainsKey(subIdx)) continue;

            int blendMode = 0;
            if (batch.MaterialIndex < m2.RenderFlags.Count)
            {
                blendMode = m2.RenderFlags[batch.MaterialIndex].BlendingMode;
            }

            map[subIdx] = blendMode;
        }

        return map;
    }

    /// <summary>
    /// Build a mapping of submeshIndex → textureIndex using the batch chain:
    ///   batch.SubmeshIndex → batch.TextureIndex → TextureLookup[idx] → texture index
    /// </summary>
    private static Dictionary<int, int> BuildSubmeshTextureMap(M2Model m2)
    {
        var map = new Dictionary<int, int>();

        foreach (var batch in m2.Batches)
        {
            int subIdx = batch.SubmeshIndex;
            if (map.ContainsKey(subIdx)) continue;

            int texIdx = 0;
            if (batch.TextureIndex < m2.TextureLookup.Count)
            {
                texIdx = m2.TextureLookup[batch.TextureIndex];
            }

            map[subIdx] = texIdx;
        }

        return map;
    }

    /// <summary>
    /// The distinct texture indices the geometry actually samples, resolved via the
    /// same batch → TextureLookup → Textures chain the material builder uses. Anything
    /// baking a recolor into the GLB must target one of THESE indices, not a Type
    /// heuristic — otherwise the recolor lands on a slot no submesh references.
    /// </summary>
    public static IReadOnlyList<int> SampledTextureIndices(M2Model m2)
        => BuildSubmeshTextureMap(m2).Values.Distinct().ToList();

    /// <summary>
    /// Build a mapping of submeshIndex → static-alpha-in-idle-pose using
    /// the batch's transparency track chain:
    ///   batch.TextureWeightIndex (= vanilla transparencyIndex)
    ///     → TransparencyLookup[idx]
    ///     → TransparencyStaticAlphas[idx]
    ///
    /// First-batch-wins on duplicates, matching the BlendMap convention.
    /// If a submesh has no batch reference, no entry is added (caller
    /// treats absence as "visible = 1.0").
    /// </summary>
    private static Dictionary<int, float> BuildSubmeshVisibilityMap(M2Model m2)
    {
        var map = new Dictionary<int, float>();

        foreach (var batch in m2.Batches)
        {
            int subIdx = batch.SubmeshIndex;
            if (map.ContainsKey(subIdx)) continue;

            map[subIdx] = m2.GetStaticAlphaForBatch(batch);
        }

        return map;
    }

    /// <summary>Simplified overload for single-texture models.</summary>
    public static bool SaveGlb(M2Model m2, byte[]? singleTexture, string outputPath,
        bool doubleSided = false)
    {
        var textures = new Dictionary<int, byte[]>();
        if (singleTexture != null) textures[0] = singleTexture;
        return SaveGlb(m2, textures, outputPath, doubleSided);
    }

    private static VERTEX MakeVertex(M2Vertex v)
    {
        return new VERTEX(
            new VertexPositionNormal(new Vector3(v.PosX, v.PosY, v.PosZ), new Vector3(v.NormX, v.NormY, v.NormZ)),
            new VertexTexture1(new Vector2(v.TexU, v.TexV))
        );
    }

    /// <summary>
    /// Convert BLP data to PNG bytes using SkiaSharp (Linux-compatible).
    /// Original extractor used System.Drawing (Windows-only GDI+).
    /// </summary>
    internal static byte[]? ConvertBlpToPngBytes(byte[] blpData)
    {
        try
        {
            var pixels = BlpDecoder.GetPixels(blpData, 0, out int w, out int h);
            if (w == 0 || h == 0 || pixels.Length == 0) return null;

            // War3Net returns BGRA pixels → SKBitmap
            using var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var bitmapPixels = bitmap.GetPixels();
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmapPixels, pixels.Length);
            bitmap.NotifyPixelsChanged();

            using var pngStream = new MemoryStream();
            bitmap.Encode(pngStream, SKEncodedImageFormat.Png, 100);
            return pngStream.ToArray();
        }
        catch { return null; }
    }
}