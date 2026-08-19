using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The single, authoritative coordinate + UV contract for the Weapon Forge, as specified in
/// WEAPON_GEN.md §2.3. Every route (parametric Route A, donor Route 0, GLB/sketch Route B)
/// funnels through these transforms exactly once, so there is one place where handedness,
/// winding, and UV convention are decided — and it is unit-testable in isolation with no
/// runtime data.
///
/// === Spaces ===
///
///  • RigidWeaponMesh authoring space — right-handed, glTF-like **Y-up**.
///      +X runs from grip toward the blade tip. The grip sits at the origin.
///      One DTO unit == one WoW model-space unit (no implicit rescale — the importer records
///      any scale/translation it applies explicitly).
///
///  • WoW M2 file space — right-handed **Z-up** (the bytes the client reads).
///
/// The preview glTF the Forge emits is authored in the SAME Y-up space as the mesh, which is
/// why <see cref="MangosSuperUI.Services.GlbWriter"/> can write mesh coordinates verbatim and why
/// <c>M2Reader</c> already converts WoW→glTF as (x,y,z)→(x,z,-y) on read. This class simply
/// names those two conversions and guarantees they are exact inverses.
///
/// === Winding / normals ===
///
/// <see cref="MeshToWoW"/> and <see cref="WoWToMesh"/> are orthonormal rotations with
/// determinant +1 (NOT reflections), so triangle winding is preserved across the conversion and
/// normals are transformed by the same rotation, then renormalized. A determinant-negative node
/// transform baked upstream (mirrored GLB node) is the only thing that flips winding, and the
/// GLB importer handles that once at ingest — never here.
///
/// === UV0 ===
///
/// Top-left image convention: (0,0) is the top-left texel corner, U increases right, V increases
/// down. UV0 is copied unchanged into both the M2 vertex and the preview glTF — neither writer
/// flips V or image rows. UV1 is exactly (0,0) for every v1 vertex.
/// </summary>
public static class CoordinateContract
{
    /// <summary>Version stamp recorded in every compiled artifact's manifest so a future change
    /// to this contract is detectable rather than silent. Bump on any change to the transforms.</summary>
    public const int Version = 1;

    /// <summary>
    /// RigidWeaponMesh (Y-up, glTF-like) → WoW M2 file space (Z-up).
    /// (x, y, z) → (x, -z, y). Determinant +1; apply identically to normals then renormalize.
    /// </summary>
    public static Vector3 MeshToWoW(Vector3 v) => new(v.X, -v.Z, v.Y);

    /// <summary>
    /// WoW M2 file space (Z-up) → RigidWeaponMesh / preview glTF (Y-up).
    /// (x, y, z) → (x, z, -y). Exact inverse of <see cref="MeshToWoW"/>. This is the same
    /// conversion M2Reader applies on read, so a donor parsed through it lands in mesh space.
    /// </summary>
    public static Vector3 WoWToMesh(Vector3 v) => new(v.X, v.Z, -v.Y);

    /// <summary>
    /// Transform a mesh-space normal into WoW space and renormalize. Because the rotation is
    /// orthonormal, this is the same rotation as position (no inverse-transpose needed here — the
    /// inverse-transpose only matters for the arbitrary node transforms the GLB importer bakes).
    /// A zero/degenerate normal is returned as +Y (a safe, finite default); callers validating
    /// input reject zero normals before reaching the writer.
    /// </summary>
    public static Vector3 MeshNormalToWoW(Vector3 n)
    {
        var r = MeshToWoW(n);
        return Normalize(r);
    }

    /// <summary>Renormalize, returning a finite unit vector. Falls back to +Y for a
    /// zero/NaN/Inf input so a single bad normal cannot emit non-finite bytes; input validation
    /// is responsible for rejecting such normals up front.</summary>
    public static Vector3 Normalize(Vector3 v)
    {
        float lenSq = v.LengthSquared();
        if (!float.IsFinite(lenSq) || lenSq < 1e-12f)
            return Vector3.UnitY;
        return v / MathF.Sqrt(lenSq);
    }

    /// <summary>
    /// True when the mesh-space triangle winding must be reversed to stay consistent after a
    /// baked node transform. Only a negative-determinant (mirrored) node transform flips winding;
    /// the Y-up↔Z-up rotation (det +1) never does. Used by the GLB importer exactly once.
    /// </summary>
    public static bool NodeTransformFlipsWinding(Matrix4x4 nodeTransform)
    {
        // The 3×3 linear part's determinant sign decides handedness. GetDeterminant() on the full
        // 4×4 equals the linear-part determinant for an affine transform (translation row is
        // (…,1)), which is all a glTF node transform ever is.
        return nodeTransform.GetDeterminant() < 0f;
    }
}
