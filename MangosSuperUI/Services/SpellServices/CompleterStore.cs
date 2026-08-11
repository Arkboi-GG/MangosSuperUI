using System.Text.Json;

namespace MangosSuperUI.Services;

/// <summary>
/// Spell Completer artifact store.
///
/// The MSUIClient creator exports a session file (spell-session.json) whose
/// spells carry their design as CONCRETE BYTES: patched effect M2s keyed by the
/// original model path, plus recolored BLPs. The Spell Completer page persists
/// those bytes here — under the same per-spell texture-cache directory the rest
/// of the patch pipeline already uses (wwwroot/images/textures/custom/{safeName})
/// — so every unified patch rebuild reproduces the design without the session
/// file being present.
///
/// Files written:
///   completer_manifest.json   — mapping of stored blobs to MPQ/model paths
///   completer_m2_{i}.bin      — patched effect M2 (keyed by ORIGINAL model path;
///                               the rebuild substitutes it for the matching
///                               cloned effect file, so the custom spell is
///                               isolated from the vanilla spell)
///   completer_extra_{i}.bin   — file added to the MPQ verbatim at its stated
///                               path: tinted BLPs at their original paths and
///                               patched GEOMETRY M2s (per-particle models whose
///                               path lives inside the host M2's bytes and
///                               cannot be re-pointed). These override the art
///                               globally — same semantics as the creator's own
///                               patch-4.MPQ export.
/// </summary>
public static class CompleterStore
{
    public const string ManifestName = "completer_manifest.json";

    public sealed class Manifest
    {
        public string TempName { get; set; } = "";
        public int SourceSpellEntry { get; set; }
        public string ExportedAtUtc { get; set; } = "";
        public List<ManifestModel> Models { get; set; } = new();
        public List<ManifestExtra> ExtraFiles { get; set; } = new();
    }

    public sealed class ManifestModel
    {
        /// <summary>Original model path as exported (e.g. Spells\Fireball_Missile_Low.m2).</summary>
        public string OriginalPath { get; set; } = "";
        public string File { get; set; } = "";
    }

    public sealed class ManifestExtra
    {
        /// <summary>MPQ path the bytes are written to verbatim.</summary>
        public string MpqPath { get; set; } = "";
        public string File { get; set; } = "";
    }

    /// <summary>Same sanitization the texture cache and LoadPatchedM2s use.</summary>
    public static string SafeName(string spellName) =>
        new(spellName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    public static string DirFor(string webRoot, string spellName) =>
        Path.Combine(webRoot, "images", "textures", "custom", SafeName(spellName));

    /// <summary>Canonical key for matching M2 paths across exporters: lowercase,
    /// backslashes, extension stripped (.m2/.mdx/.mdl are the same model).</summary>
    public static string NormalizeM2Key(string path)
    {
        string p = path.Replace('/', '\\').Trim().ToLowerInvariant();
        if (p.EndsWith(".m2") || p.EndsWith(".mdx") || p.EndsWith(".mdl"))
            p = p[..p.LastIndexOf('.')];
        return p;
    }

    /// <summary>Persist a completed spell's design bytes. Replaces any previous
    /// completer artifacts for this spell (re-completing is idempotent).</summary>
    public static void Save(string webRoot, string spellName, Manifest manifestMeta,
        List<(string originalPath, byte[] bytes)> models,
        List<(string mpqPath, byte[] bytes)> extraFiles)
    {
        string dir = DirFor(webRoot, spellName);
        Directory.CreateDirectory(dir);

        foreach (string stale in Directory.GetFiles(dir, "completer_*"))
            File.Delete(stale);

        var manifest = new Manifest
        {
            TempName = manifestMeta.TempName,
            SourceSpellEntry = manifestMeta.SourceSpellEntry,
            ExportedAtUtc = manifestMeta.ExportedAtUtc,
        };
        for (int i = 0; i < models.Count; i++)
        {
            string file = $"completer_m2_{i}.bin";
            File.WriteAllBytes(Path.Combine(dir, file), models[i].bytes);
            manifest.Models.Add(new ManifestModel { OriginalPath = models[i].originalPath, File = file });
        }
        for (int i = 0; i < extraFiles.Count; i++)
        {
            string file = $"completer_extra_{i}.bin";
            File.WriteAllBytes(Path.Combine(dir, file), extraFiles[i].bytes);
            manifest.ExtraFiles.Add(new ManifestExtra { MpqPath = extraFiles[i].mpqPath, File = file });
        }

        File.WriteAllText(Path.Combine(dir, ManifestName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Manifest? LoadManifest(string webRoot, string spellName)
    {
        string path = Path.Combine(DirFor(webRoot, spellName), ManifestName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Patched effect M2s keyed by NormalizeM2Key(original path), for
    /// SpellPatchRequest.PerPathPatchedM2s. Null when this spell has none.</summary>
    public static Dictionary<string, byte[]>? LoadPerPathM2s(string webRoot, string spellName)
    {
        var manifest = LoadManifest(webRoot, spellName);
        if (manifest is null || manifest.Models.Count == 0) return null;
        string dir = DirFor(webRoot, spellName);
        var result = new Dictionary<string, byte[]>();
        foreach (var model in manifest.Models)
        {
            string file = Path.Combine(dir, model.File);
            if (File.Exists(file))
                result[NormalizeM2Key(model.OriginalPath)] = File.ReadAllBytes(file);
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Verbatim MPQ files (tinted BLPs, geometry M2s), for
    /// SpellPatchRequest.ExtraMpqFiles. Null when this spell has none.</summary>
    public static Dictionary<string, byte[]>? LoadExtraFiles(string webRoot, string spellName)
    {
        var manifest = LoadManifest(webRoot, spellName);
        if (manifest is null || manifest.ExtraFiles.Count == 0) return null;
        string dir = DirFor(webRoot, spellName);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in manifest.ExtraFiles)
        {
            string file = Path.Combine(dir, extra.File);
            if (File.Exists(file))
                result[extra.MpqPath] = File.ReadAllBytes(file);
        }
        return result.Count > 0 ? result : null;
    }
}
