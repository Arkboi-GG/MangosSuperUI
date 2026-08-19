namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Turns a sketch/concept image into a GLB. The default implementation
/// (<see cref="ComfyUIWeapon3DGenerator"/>) runs an image→3D graph on the ComfyUI pool the app already
/// uses (Settings → AI Services) — so there is no separate service to configure. The resulting GLB
/// flows into <see cref="GlbWeaponImporter"/> exactly like any other, whether it came from a sketch,
/// a FLUX concept, or a hand-modelled export.
/// </summary>
public interface IWeapon3DGenerator
{
    /// <summary>True when image→3D is ready to run (e.g. the workflow is present). When false, the UI
    /// tells the user to upload a GLB manually instead.</summary>
    bool IsConfigured { get; }

    /// <summary>Reconstruct a front-facing weapon image into a GLB. Returns Ok=false with a plain-English
    /// reason (what to fix) when it can't — never throws for expected setup gaps.</summary>
    Task<Weapon3DGenerationResult> GenerateGlbAsync(byte[] imageBytes, CancellationToken ct = default);
}

public sealed record Weapon3DGenerationResult(bool Ok, byte[]? Glb, string Message);
