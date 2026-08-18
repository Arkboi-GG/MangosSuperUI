using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The writer-owned mesh AST for a single rigid weapon (WEAPON_GEN.md §2.3, §4.1). This is
/// deliberately NOT the lossy GLB-preview <c>M2Model</c>: it is the one input contract the pure
/// <c>WeaponAssetCompiler</c> accepts, and every route (parametric Route A, donor Route 0,
/// GLB/sketch Route B) produces one of these.
///
/// Authoring space is right-handed **Y-up** (see <see cref="CoordinateContract"/>): +X grip→tip,
/// grip at the origin, one unit == one WoW unit. Exactly one triangle-list primitive, one
/// material, UV0 present, UV1 implicitly (0,0). Positions/normals/UV0 are parallel arrays indexed
/// by vertex; <see cref="Indices"/> is a flat triangle list (multiple of 3).
/// </summary>
public sealed class RigidWeaponMesh
{
    /// <summary>Vertex positions, Y-up mesh space.</summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>Per-vertex normals, Y-up mesh space. Must be finite and non-zero (validated).</summary>
    public required Vector3[] Normals { get; init; }

    /// <summary>Per-vertex UV0, top-left image convention (U right, V down).</summary>
    public required Vector2[] Uv0 { get; init; }

    /// <summary>Flat triangle-list indices into the vertex arrays. Length is a multiple of 3.</summary>
    public required uint[] Indices { get; init; }

    /// <summary>
    /// Stable per-vertex identity, parallel to <see cref="Positions"/>. Fixed-topology phases
    /// (0–4) require these to match the golden donor's 34 vertex IDs exactly so an offset-preserving
    /// edit can be proven not to have lost/duplicated/reordered a vertex. Null for freshly generated
    /// variable topology (Phase 5+).
    /// </summary>
    public int[]? VertexIds { get; init; }

    /// <summary>The single material contract for v1: one opaque base pass, one Type-2 texture.</summary>
    public required WeaponMaterial Material { get; init; }

    /// <summary>
    /// Optional per-triangle semantic region label (blade/edge/fuller/guard/grip/pommel …),
    /// parallel to the triangle list (one entry per triangle). Route A supplies these so the
    /// compiler can emit region masks; Route B normally leaves it null. The compiler never
    /// fabricates semantic regions from UVs.
    /// </summary>
    public string[]? TriangleRegionIds { get; init; }

    /// <summary>
    /// What the importer/generator did to land this mesh in the canonical envelope — recorded, not
    /// guessed. Copied into the artifact manifest for reproducibility.
    /// </summary>
    public MeshNormalizationRecord Normalization { get; init; } = MeshNormalizationRecord.Identity;

    public int VertexCount => Positions.Length;
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>The v1 material: one opaque base render pass bound to one Type-2 (empty-filename) M2
/// texture slot whose pixels come from ItemDisplayInfo.TextureName1. DXT3/alpha/multi-pass are
/// out of scope for v1 and are represented by later additions, not by overloading this.</summary>
public sealed class WeaponMaterial
{
    /// <summary>Opaque base pass — the only v1 blend mode. Present as an explicit field so a future
    /// alpha pass is an added value, not a silent reinterpretation.</summary>
    public WeaponBlendMode BlendMode { get; init; } = WeaponBlendMode.Opaque;

    /// <summary>Two-sided rendering. Weapons are single-sided in vanilla; kept false for v1.</summary>
    public bool TwoSided { get; init; } = false;
}

public enum WeaponBlendMode
{
    /// <summary>Opaque base pass, no alpha (v1). Maps to M2 render flag / DXT1.</summary>
    Opaque = 0,
}

/// <summary>Explicit record of the affine normalization applied to bring source geometry into the
/// canonical grip-at-origin, +X-blade, WoW-unit envelope. Identity when the generator authored
/// directly in canonical space (Route A).</summary>
public sealed class MeshNormalizationRecord
{
    public float Scale { get; init; } = 1f;
    public Vector3 Translation { get; init; } = Vector3.Zero;
    /// <summary>True if the importer reversed triangle winding once for a mirrored (negative
    /// determinant) source node transform. Recorded so the operation is auditable.</summary>
    public bool WindingReversed { get; init; }
    /// <summary>Free-form note on how orientation/scale were determined (e.g. "authored canonical",
    /// "PCA long-axis → +X, grip end at min-X", "explicit owner grip marker").</summary>
    public string Method { get; init; } = "identity";

    public static MeshNormalizationRecord Identity { get; } = new() { Method = "identity" };
}
