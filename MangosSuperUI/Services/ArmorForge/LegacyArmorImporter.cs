using System.Numerics;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using MangosSuperUI.Services.WeaponForge.Motion;
using MangosSuperUI.Services.WeaponForge.RawM2;
using SkiaSharp;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// Turns a TBC (2.4.3) armor item into a forge-ready <see cref="ArmorImportSource"/> (ARMOR_FORGE.md
/// §3). One resolver per render lane, all measured against the local 1.12 + 2.4.3 clients:
///
///   • PAINTED (chest/legs/gloves/boots/bracers/belt/shirt/tabard/robe): the TBC row's m_texture[]
///     partials → every gender variant BLP (_M/_F/_U/bare) pulled from TBC
///     <c>Item\TextureComponents\{subdir}\</c> and carried BYTE-FOR-BYTE when it is a BLP2 the vanilla
///     texture path accepts (same envelope check the Weapon Forge uses for TBC weapon skins); else
///     decoded and re-encoded uncompressed. The vanilla client composites them exactly as its own.
///
///   • HELM: the DBC names one logical model ("Helm_X.mdx"); the client loads
///     <c>Helm_X_{HuM,HuF,OrM,…}.m2</c> per race/gender. TBC uses the same scheme, so every one of
///     vanilla's 16 variants is re-emitted from the TBC variant of the SAME race/gender (falling back
///     to the HuM mesh when TBC lacks one) onto a stock vanilla helm donor scaffold — ideally the
///     vanilla file of the same stem, else a pinned plain helm of that race/gender. Emission is the
///     Weapon Forge's proven chain: M2Reader.Parse (v260 ok) → LegacyWeaponMeshExtractor.Extract →
///     CoordinateContract.MeshToWoW → M2VariableTopologyBuilder.Build → RewriteInternalName →
///     M2BinaryValidator. None of that is weapon-specific; orientation is a byte-space identity
///     (helms/shoulders are placed by attachment, not a grip envelope).
///
///   • SHOULDER: an L/R PAIR of distinct files (stock ModelName1=LShoulder_X.mdx,
///     ModelName2=RShoulder_X.mdx) sharing ONE texture — both re-emitted, each on its own side's
///     stock donor.
///
///   • CLOAK: texture-only — the TBC Cape\{TextureName1}.blp carried across (no M2 in either client).
///
/// Geoset groups (6-8), helmet hair/facial visibility (12-13), group sound (11) and icon (5) are
/// carried from the TBC row — fields 0..22 are identical in both layouts.
/// </summary>
public abstract class LegacyArmorImporter
{
    private readonly LegacyArmorCatalog _catalog;
    private readonly MpqReaderService _vanilla;
    private readonly ILogger _logger;

    // Pinned plain vanilla donors (1 bone / 4 views / 1 texture, measured) when the same-stem
    // vanilla file doesn't exist. Every one of these exists in the stock 1.12 client.
    private const string DefaultHelmStem = "Helm_Leather_D_01";
    private const string DefaultShoulderLeft = "LShoulder_Leather_A_01";
    private const string DefaultShoulderRight = "RShoulder_Leather_A_01";

    protected LegacyArmorImporter(LegacyArmorCatalog catalog, MpqReaderService vanilla, ILogger logger)
    {
        _catalog = catalog;
        _vanilla = vanilla;
        _logger = logger;
    }

    /// <summary>Lane key / label of the later client this importer reads from.</summary>
    public string Key => _catalog.Key;
    public string Label => _catalog.Label;
    /// <summary>The browse catalog this importer resolves entries against.</summary>
    public LegacyArmorCatalog Catalog => _catalog;

    /// <summary>Resolve a TBC armor entry into a forge source. <paramref name="displayIndex"/> is the
    /// reserved display id (= SUI_A model index) so emitted member paths/internal names are final.</summary>
    public ArmorImportSource? Resolve(uint entry, int displayIndex, ForgeDiagnostics diag, Vector3? glowColor = null, float glowIntensity = 1f)
    {
        var item = _catalog.FindEntry(entry);
        if (item is null) { diag.Error("import.entry", $"{Label} entry {entry} is not a browsable armor item."); return null; }
        var row = _catalog.GetDisplayRow(item.DisplayId);
        if (row is null) { diag.Error("import.display", $"{Label} display {item.DisplayId} not found — is the {Label} client mounted?"); return null; }
        var profile = ArmorTypeCatalog.Get(item.FamilyKey);

        var src = new ArmorImportSource
        {
            Entry = entry, Name = item.Name, FamilyKey = item.FamilyKey, RenderKind = profile.RenderKind,
            Material = item.Material, Quality = item.Quality, ItemLevel = item.ItemLevel, RequiredLevel = item.RequiredLevel,
            IconStem = row.IconStem, GeosetGroup = row.GeosetGroup, GroupSoundIndex = row.GroupSoundIndex,
            HelmetVis0 = row.HelmetVis0, HelmetVis1 = row.HelmetVis1, SetId = item.SetId, SetName = item.SetName,
        };

        switch (profile.RenderKind)
        {
            case ArmorRenderKind.Painted: return ResolvePainted(src, row, profile, displayIndex, diag) ? src : null;
            case ArmorRenderKind.Cloak: return ResolveCloak(src, row, displayIndex, diag) ? src : null;
            case ArmorRenderKind.Modelled:
                return (item.FamilyKey == "helm" ? ResolveHelm(src, row, displayIndex, diag, glowColor, glowIntensity) : ResolveShoulder(src, row, displayIndex, diag, glowColor, glowIntensity)) ? src : null;
        }
        return null;
    }

    // ── painted ────────────────────────────────────────────────────────

    private static readonly string[] GenderSuffixes = { "_M", "_F", "_U", "" };

    private bool ResolvePainted(ArmorImportSource src, LegacyDisplayRow row, ArmorTypeProfile profile, int displayIndex, ForgeDiagnostics diag)
    {
        // ONLY the slots this equip type legitimately paints (profile.PaintedSlots). Later-client rows
        // are frequently authored from a shared set template and list textures for other slots (a
        // gauntlet row carrying chest/pant/boot textures); the game client ignores those, and so must
        // we — carrying them would make each forged piece overpaint its neighbours.
        foreach (int slot in profile.PaintedSlots)
        {
            if (slot < 0 || slot > 7) continue;
            string partial = row.ComponentPartials[slot];
            if (string.IsNullOrEmpty(partial)) continue;
            string subdir = ArmorNaming.ComponentSubdirs[slot];
            bool any = false;
            foreach (var suffix in GenderSuffixes)
            {
                byte[]? blp = _catalog.ExtractFile($@"Item\TextureComponents\{subdir}\{partial}{suffix}.blp");
                if (blp is not { Length: > 0 }) continue;
                byte[]? packed = PackComponentBlp(blp, slot, diag, $"slot {slot}{suffix}");
                if (packed is null) continue;
                // Pack under OUR stem with the SAME gender suffix (bare → _U, which the client also
                // tries), so male/female art stays distinct instead of collapsing to one.
                string outSuffix = suffix.Length == 0 ? "_U" : suffix;
                if (src.Components.Any(c => c.Slot == slot && c.GenderSuffix == outSuffix)) continue; // _U and bare both present
                src.Components.Add(new ArmorComponentBlob
                {
                    Slot = slot, GenderSuffix = outSuffix, Blp = packed,
                    MpqPath = $@"Item\TextureComponents\{subdir}\{ArmorNaming.ComponentStem(displayIndex, slot)}{outSuffix}.blp",
                });
                any = true;
            }
            if (!any) diag.Warn("import.component.missing", $"Slot {slot} ('{partial}') has no extractable BLP in the {Label} client.");
        }
        if (src.Components.Count == 0)
        {
            diag.Error("import.painted.empty", $"'{src.Name}' has no extractable body-atlas textures.");
            return false;
        }
        AttachIcon(src, row, diag);
        return true;
    }

    /// <summary>The vanilla body-atlas region each component slot is composited into (measured on the
    /// 1.12 client: ArmUpper/ArmLower/TorsoUpper/LegUpper/LegLower 128×64, Hand/TorsoLower/Foot 128×32;
    /// every vanilla and TBC component matches, 3.3.5a ships 2× versions for late sets). The 1.12
    /// client blits a component at region size, so anything larger has to be downscaled to the
    /// region — the upper limit the client can show — or only its top-left quarter lands on the body.</summary>
    public static (int Width, int Height) ComponentRegion(int slot) => slot switch
    {
        2 or 4 or 7 => (128, 32),
        _ => (128, 64),
    };

    /// <summary>Byte-for-byte when the BLP2 envelope is one vanilla accepts AND the image fits the
    /// slot's atlas region; else decode → (downscale to the region, high-quality resample) → uncompressed.</summary>
    private byte[]? PackComponentBlp(byte[] blp, int slot, ForgeDiagnostics diag, string what)
    {
        var (rw, rh) = ComponentRegion(slot);
        int srcW = blp.Length >= 20 ? BitConverter.ToInt32(blp, 12) : 0, srcH = blp.Length >= 20 ? BitConverter.ToInt32(blp, 16) : 0;
        bool oversized = srcW > rw || srcH > rh;
        if (!oversized && WeaponAssetCompiler.ValidateBlp2Envelope(blp) is null) return blp;
        try
        {
            var pixels = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmp.GetPixels(), pixels.Length);
            bmp.NotifyPixelsChanged();
            SKBitmap toEncode = bmp;
            SKBitmap? resized = null;
            if (w > rw || h > rh)
            {
                // Keep as much as the atlas can take: scale to the region exactly (aspect matches — the
                // later clients doubled both axes), Mitchell resample so chain links and trim survive.
                resized = bmp.Resize(new SKImageInfo(Math.Min(w, rw), Math.Min(h, rh), SKColorType.Bgra8888, SKAlphaType.Unpremul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is null) { diag.Warn("import.component.resize", $"{what}: {w}×{h} could not be resized to the {rw}×{rh} atlas region; skipped."); return null; }
                toEncode = resized;
            }
            var re = new BlpWriterService().EncodeBitmapToBlpUncompressed(toEncode);
            resized?.Dispose();
            if (re is null) { diag.Warn("import.component.reencode", $"{what}: re-encode failed; skipped."); return null; }
            diag.Info("import.component.reencode", oversized
                ? $"{what}: {w}×{h} source downscaled to the vanilla {rw}×{rh} atlas region (the client would otherwise show only its top-left quarter)."
                : $"{what}: non-vanilla BLP2 envelope, re-encoded uncompressed.");
            return re;
        }
        catch (Exception ex)
        {
            diag.Warn("import.component.decode", $"{what}: BLP decode failed ({ex.Message}); skipped.");
            return null;
        }
    }

    // ── cloak ──────────────────────────────────────────────────────────

    private bool ResolveCloak(ArmorImportSource src, LegacyDisplayRow row, int displayIndex, ForgeDiagnostics diag)
    {
        if (string.IsNullOrEmpty(row.TextureName1)) { diag.Error("import.cloak.texture", "Cloak row has no TextureName1."); return false; }
        byte[]? blp = _catalog.ExtractFile($@"{ArmorNaming.CapeDir}\{row.TextureName1}.blp");
        if (blp is not { Length: > 0 }) { diag.Error("import.cloak.blp", $"Cape texture '{row.TextureName1}' not in the {Label} client."); return false; }
        byte[]? packed = PackModelBlp(blp, diag, "cloak");
        if (packed is null) return false;
        src.TextureName = ArmorNaming.TextureStem(displayIndex);
        src.TextureMpqPath = $@"{ArmorNaming.CapeDir}\{ArmorNaming.TextureStem(displayIndex)}.blp";
        src.TextureBlp = packed;
        AttachIcon(src, row, diag);
        return true;
    }

    /// <summary>Model/cape skins: byte-for-byte BLP2 when valid, else decode → 256² DXT1.</summary>
    private byte[]? PackModelBlp(byte[] blp, ForgeDiagnostics diag, string what)
    {
        if (WeaponAssetCompiler.ValidateBlp2Envelope(blp) is null) return blp;
        try
        {
            var pixels = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmp.GetPixels(), pixels.Length);
            bmp.NotifyPixelsChanged();
            var writer = new BlpWriterService();
            var re = writer.EncodeBitmapToBlp(bmp, useDxt1: true);
            if (re is null) diag.Warn("import.skin.reencode", $"{what}: non-vanilla BLP2 envelope and DXT1 re-encode failed ({w}×{h}).");
            else diag.Info("import.skin.reencode", $"{what}: non-vanilla BLP2 envelope, re-encoded DXT1.");
            return re;
        }
        catch (Exception ex) { diag.Warn("import.skin.decode", $"{what}: BLP decode failed ({ex.Message})."); return null; }
    }

    // ── helm ───────────────────────────────────────────────────────────

    private bool ResolveHelm(ArmorImportSource src, LegacyDisplayRow row, int displayIndex, ForgeDiagnostics diag, Vector3? glowColor = null, float glowIntensity = 1f)
    {
        string stem = StripStem(row.ModelName1);
        if (stem.Length == 0) { diag.Error("import.helm.model", "Helm row has no ModelName1."); return false; }

        // Texture (one, shared by all variants).
        if (!ResolveModelTexture(src, row.TextureName1, ArmorNaming.HeadDir, displayIndex, diag, "helm")) return false;

        // Pass 1: parse every TBC variant; pick the fallback mesh (HuM preferred, else first parsed)
        // BEFORE emitting so an early-missing variant still gets a file.
        var parsed = new Dictionary<string, M2Model>(StringComparer.OrdinalIgnoreCase);
        foreach (var suffix in ArmorNaming.HelmVariantSuffixes)
        {
            var m2 = ParseSourceModel($@"{ArmorNaming.HeadDir}\{stem}_{suffix}.m2");
            if (m2 is not null) parsed[suffix] = m2;
        }
        if (parsed.Count == 0) { diag.Error("import.helm.none", $"{Label} has no parseable variant of {stem}."); return false; }
        string fallbackSuffix = parsed.ContainsKey("HuM") ? "HuM" : parsed.Keys.First();
        var fallback = parsed[fallbackSuffix];
        bool stripMasks = DecideSkinAlphaPolicy(src, parsed.Values, diag, "helm");

        // Pass 2: emit all 16. A single bad race/gender variant is a WARNING (that race sees the
        // fallback or nothing), not a failure of the whole helm — only zero emitted fails.
        var effects = new EffectTextureMap();
        int emitted = 0, fellBack = 0;
        foreach (var suffix in ArmorNaming.HelmVariantSuffixes)
        {
            if (!parsed.TryGetValue(suffix, out var m2))
            {
                m2 = fallback; fellBack++;
                diag.Info("import.helm.variant", $"{suffix}: {Label} variant missing — emitted from the {fallbackSuffix} mesh.");
            }

            // Donor: same-stem vanilla file of this race/gender, else the pinned plain helm variant.
            byte[]? donor = _vanilla.ExtractFile($@"{ArmorNaming.HeadDir}\{stem}_{suffix}.m2")
                         ?? _vanilla.ExtractFile($@"{ArmorNaming.HeadDir}\{DefaultHelmStem}_{suffix}.m2");
            if (donor is null) { diag.Warn("import.helm.donor", $"{suffix}: no vanilla donor helm — this race/gender ships no file."); continue; }

            var vdiag = new ForgeDiagnostics("helm-" + suffix);
            var bytes = Emit(m2, donor, $"{ArmorNaming.ModelStem(displayIndex)}_{suffix}", ArmorNaming.HeadDir, displayIndex, effects, vdiag, $"helm {suffix}", glowColor, glowIntensity,
                stripReflectionMasks: stripMasks);
            // 16 variants repeat the same emitter/bake notes — keep one copy of each distinct message.
            foreach (var item in vdiag.Items.Where(i => i.Severity != ForgeSeverity.Error))
                if (!diag.Items.Any(x => x.Code == item.Code && x.Message == item.Message)) diag.Add(item.Severity, item.Code, item.Message, item.Context);
            if (bytes is null)
            {
                diag.Warn("import.helm.variant.failed", $"{suffix}: " + string.Join("; ", vdiag.Items.Where(i => i.Severity == ForgeSeverity.Error).Select(i => i.Message)));
                continue;
            }
            src.ModelMembers.Add(new MpqMember { MpqPath = ArmorNaming.HelmVariantMpqPath(displayIndex, suffix), Data = bytes });
            emitted++;
        }
        src.ModelMembers.AddRange(effects.Members);
        if (emitted == 0) { diag.Error("import.helm.none", "No helm variant could be emitted."); return false; }
        if (emitted < ArmorNaming.HelmVariantSuffixes.Count) diag.Warn("import.helm.partial", $"{emitted}/{ArmorNaming.HelmVariantSuffixes.Count} race/gender variants emitted.");
        if (fellBack > 0) diag.Warn("import.helm.fallback", $"{fellBack} race/gender variant(s) used the {fallbackSuffix} mesh.");
        if (stripMasks) FlattenSkinAlpha(src, diag, "helm");
        AttachIcon(src, row, diag);
        src.ModelName = ArmorNaming.DbcModelName(displayIndex); // "SUI_A_####.mdx" — client appends _{Ra}{G}
        src.ModelName2 = null;
        return true;
    }

    // ── shoulder ───────────────────────────────────────────────────────

    private bool ResolveShoulder(ArmorImportSource src, LegacyDisplayRow row, int displayIndex, ForgeDiagnostics diag, Vector3? glowColor = null, float glowIntensity = 1f)
    {
        string left = StripStem(row.ModelName1);
        string right = StripStem(row.ModelName2);
        if (left.Length == 0) { diag.Error("import.shoulder.model", "Shoulder row has no ModelName1."); return false; }
        // Stock rows name both sides; a single-sided row mirrors the left file to the right slot.
        if (right.Length == 0) right = left.StartsWith("L", StringComparison.OrdinalIgnoreCase) ? "R" + left[1..] : left;

        if (!ResolveModelTexture(src, row.TextureName1, ArmorNaming.ShoulderDir, displayIndex, diag, "shoulder")) return false;

        var effects = new EffectTextureMap();
        var lm = ParseSourceModel($@"{ArmorNaming.ShoulderDir}\{left}.m2");
        var rm = ParseSourceModel($@"{ArmorNaming.ShoulderDir}\{right}.m2") ?? lm;
        if (lm is null) { diag.Error("tbc.shoulder.m2", $"{Label} shoulder '{left}' not found/parsed."); return false; }

        byte[]? ld = _vanilla.ExtractFile($@"{ArmorNaming.ShoulderDir}\{left}.m2") ?? _vanilla.ExtractFile($@"{ArmorNaming.ShoulderDir}\{DefaultShoulderLeft}.m2");
        byte[]? rd = _vanilla.ExtractFile($@"{ArmorNaming.ShoulderDir}\{right}.m2") ?? _vanilla.ExtractFile($@"{ArmorNaming.ShoulderDir}\{DefaultShoulderRight}.m2");
        if (ld is null || rd is null) { diag.Error("import.shoulder.donor", "No vanilla shoulder donor found."); return false; }
        bool stripMasks = DecideSkinAlphaPolicy(src, new[] { lm, rm! }, diag, "shoulder");

        var lb = Emit(lm, ld, $"{ArmorNaming.ModelStem(displayIndex)}_L", ArmorNaming.ShoulderDir, displayIndex, effects, diag, "shoulder L", glowColor, glowIntensity, stripReflectionMasks: stripMasks);
        var rb = Emit(rm!, rd, $"{ArmorNaming.ModelStem(displayIndex)}_R", ArmorNaming.ShoulderDir, displayIndex, effects, diag, "shoulder R", glowColor, glowIntensity, stripReflectionMasks: stripMasks);
        if (lb is null || rb is null) return false;
        if (stripMasks) FlattenSkinAlpha(src, diag, "shoulder");
        src.ModelMembers.Add(new MpqMember { MpqPath = ArmorNaming.ShoulderLeftMpqPath(displayIndex), Data = lb });
        src.ModelMembers.Add(new MpqMember { MpqPath = ArmorNaming.ShoulderRightMpqPath(displayIndex), Data = rb });
        src.ModelMembers.AddRange(effects.Members);
        src.ModelName = ArmorNaming.ShoulderLeftDbcName(displayIndex);
        src.ModelName2 = ArmorNaming.ShoulderRightDbcName(displayIndex);
        AttachIcon(src, row, diag);
        return true;
    }

    /// <summary>Effect (hardcoded Type-0) textures keyed by their TBC SOURCE path, so every variant
    /// (16 helm files, L/R shoulders) that references the same TBC file gets the same packaged member,
    /// and two variants that reference different files never collide on a slot index.</summary>
    private sealed class EffectTextureMap
    {
        private readonly Dictionary<string, (string OutPath, byte[] Data)> _bySource = new(StringComparer.OrdinalIgnoreCase);
        public IEnumerable<MpqMember> Members => _bySource.Values.Select(v => new MpqMember { MpqPath = v.OutPath, Data = v.Data });
        public int Count => _bySource.Count;
        public bool TryGet(string sourcePath, out string outPath)
        {
            if (_bySource.TryGetValue(sourcePath, out var v)) { outPath = v.OutPath; return true; }
            outPath = ""; return false;
        }
        public void Add(string sourcePath, string outPath, byte[] data) => _bySource[sourcePath] = (outPath, data);
    }

    /// <summary>Carry the TBC bag icon when vanilla lacks it (else the bag shows a blank icon).</summary>
    private void AttachIcon(ArmorImportSource src, LegacyDisplayRow row, ForgeDiagnostics diag)
    {
        if (string.IsNullOrEmpty(row.IconStem)) return;
        string member = $@"Interface\Icons\{row.IconStem}.blp";
        // "Stock" must mean Blizzard's own archives (base data + patch/patch-2). The mounted client
        // dir also holds the forge's OWN deployed patches, and an icon that a previous import
        // packaged into them reads back as present — this piece then skips packaging, and the icon
        // vanishes on the next registry rebuild once the piece that DID carry it is deleted or
        // re-imported (measured 2026-08-24: blank bag icons after a delete + re-import cycle).
        int stockCeiling = Mpq.MpqPatchOrder.Rank("patch-2.MPQ");
        if (_vanilla.ExtractFile(member, skipArchive: n => Mpq.MpqPatchOrder.Rank(n) > stockCeiling) is not null)
            return; // stock icon exists
        var blp = _catalog.ExtractFile(member);
        if (blp is not { Length: > 0 }) { diag.Warn("import.icon.missing", $"Icon '{row.IconStem}' not in vanilla or {Label} — bag icon will be blank."); return; }
        var packed = PackModelBlp(blp, diag, "icon");
        if (packed is null) return;
        src.ModelMembers.Add(new MpqMember { MpqPath = member, Data = packed });
        diag.Info("import.icon", $"{Label}-only icon '{row.IconStem}' packaged.");
    }

    // ── shared model helpers ───────────────────────────────────────────

    private bool ResolveModelTexture(ArmorImportSource src, string texStem, string dir, int displayIndex, ForgeDiagnostics diag, string what)
    {
        if (string.IsNullOrEmpty(texStem)) { diag.Error($"tbc.{what}.texture", $"{what} row has no TextureName1."); return false; }
        byte[]? blp = _catalog.ExtractFile($@"{dir}\{texStem}.blp");
        if (blp is not { Length: > 0 }) { diag.Error($"tbc.{what}.blp", $"{what} texture '{texStem}' not in the {Label} client."); return false; }
        var packed = PackModelBlp(blp, diag, what);
        if (packed is null) return false;
        src.TextureName = ArmorNaming.TextureStem(displayIndex);
        src.TextureMpqPath = ArmorNaming.TextureMpqPath(displayIndex, dir);
        src.TextureBlp = packed;
        return true;
    }

    /// <summary>Plan the animated rebuild of this model's particle effects. Positions come straight
    /// from the source in mesh space — armor pieces are emitted without a placement transform, so the
    /// emitter sits exactly where the later client had it.</summary>
    private EffectMotionPlanner.Plan PlanMotion(M2Model m2, string what)
    {
        if (m2.ParticleEmitters.Count == 0)
            return new EffectMotionPlanner.Plan(Array.Empty<M2EmitterTransplanter.Graft>(), Array.Empty<string>(), 0);
        var posWoW = m2.ParticleEmitters.Select(e => CoordinateContract.MeshToWoW(e.Position)).ToList();
        return EffectMotionPlanner.Build(m2.ParticleEmitters, posWoW, path => _vanilla.ExtractFile(path), null, what);
    }

    /// <summary>Version-aware parse, either lane: TBC v260 inline views, or WotLK v264 + its .skin
    /// profile. Which one is decided by the mounted client, not by the caller.</summary>
    private M2Model? ParseSourceModel(string mpqPath)
    {
        try { return _catalog.LoadM2(mpqPath); } catch { return null; }
    }

    /// <summary>Preview emitters for a not-yet-imported piece: the SAME donor-graft plan the import
    /// bakes (<see cref="PlanMotion"/> → M2EmitterTransplanter), converted for the GLB preview via
    /// <see cref="M2FxReader.FromGraft"/> — donor curves plus the source's position/colour/size/timing
    /// overrides. This is what makes the pre-import preview show the effect the forge will PRODUCE:
    /// the raw WotLK emitter summary has no scale/alpha curves and no flipbook ranges, and rendering
    /// it directly drew giant flat white columns where the committed piece shows small wisps.</summary>
    public List<WeaponPreviewService.PreviewEmitter>? PlanPreviewEmitters(M2Model m2)
    {
        try
        {
            var plan = PlanMotion(m2, "preview");
            if (!plan.Any) return null;
            var result = new List<WeaponPreviewService.PreviewEmitter>();
            var pngCache = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var graft in plan.Grafts)
            {
                string path = graft.TexturePath ?? "";
                if (path.Length == 0) continue;
                if (!pngCache.TryGetValue(path, out var png))
                {
                    var blp = _vanilla.ExtractFile(path) ?? _catalog.ExtractFile(path);
                    png = blp is { Length: > 0 } ? BlpToPngBytes(blp) : null;
                    pngCache[path] = png;
                }
                if (png is not { Length: > 0 }) continue;
                result.Add(new WeaponPreviewService.PreviewEmitter(
                    graft, CoordinateContract.WoWToMesh(graft.PositionWoW), png));
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    private static byte[]? BlpToPngBytes(byte[] blp)
    {
        try
        {
            var px = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            if (w == 0 || h == 0) return null;
            using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
            bmp.NotifyPixelsChanged();
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Re-emit one TBC M2 onto a vanilla donor scaffold. Effect (hardcoded Type-0) textures
    /// are pulled from TBC and packed under SUI_A effect paths, shared across variants via
    /// <paramref name="effects"/>.</summary>
    private byte[]? Emit(M2Model m2, byte[] donor, string internalName, string componentDir, int displayIndex,
        EffectTextureMap effects, ForgeDiagnostics diag, string what, Vector3? glowColor = null, float glowIntensity = 1f,
        bool stripReflectionMasks = false)
    {
        try
        {
            // Armor is where frozen effects hurt most: a helm's eye flames and a shoulder's braziers
            // are the whole point of the piece, and a still picture of fire reads as a sticker. Plan
            // the animated rebuild BEFORE extracting, because a rebuilt emitter must not ALSO be
            // baked into a static sprite — that would draw the effect twice.
            var motionPlan = PlanMotion(m2, what);
            var extracted = LegacyWeaponMeshExtractor.Extract(m2, diag, Label, bakeEmitters: !motionPlan.Any);
            if (extracted is null) { diag.Error("import.extract", $"{what}: mesh extraction failed."); return null; }
            if (motionPlan.Any)
            {
                foreach (var note in motionPlan.Notes) diag.Info("motion.emitter", note);
                diag.Info("motion.plan", $"{what}: {motionPlan.SourceEmitterCount} source particle emitter(s) rebuilt as animated 1.12 emitters instead of static sprites.");
            }
            var mesh = extracted.Mesh;

            // Reflection-mask layers (see ImportedSkinAlphaPolicy): decided once per piece by
            // DecideSkinAlphaPolicy so the packaged skin and every variant's pass list agree.
            if (stripReflectionMasks && mesh.Passes is { Count: > 0 } sourcePasses)
            {
                var policy = ImportedSkinAlphaPolicy.Apply(sourcePasses);
                if (policy.StrippedMaskPasses > 0)
                {
                    mesh = ImportedSkinAlphaPolicy.WithPasses(mesh, policy.Passes);
                    diag.Info("import.skin.mask.dropped",
                        $"{what}: {policy.StrippedMaskPasses} alpha-blended reflection-mask layer(s) dropped — the skin's alpha channel " +
                        "is a shininess mask the 1.12 character frame renders as transparency; the reflection now shows unmasked.");
                }
            }

            // Effect textures: slot 0 is the replaceable display texture; slots ≥1 are hardcoded files.
            // Packaged members are keyed by the TBC SOURCE file (not the slot index) so variants that
            // order their textures differently still bind the right bytes.
            var effectPaths = new List<string>();
            for (int i = 1; i < extracted.SourceTextures.Count; i++)
            {
                var st = extracted.SourceTextures[i];
                string key = st.SourcePath ?? $"<slot{i}>";
                if (!effects.TryGet(key, out string outPath))
                {
                    outPath = $@"{componentDir}\{ArmorNaming.ModelStem(displayIndex)}_E{effects.Count + 1:D2}.blp";
                    byte[]? eb = st.SourcePath is null ? null : _catalog.ExtractFile(st.SourcePath);
                    byte[]? packed = eb is { Length: > 0 } ? PackModelBlp(eb, diag, $"{what} effect {i}") : null;
                    if (packed is null) diag.Warn("import.effect.missing", $"{what}: effect texture {i} ('{st.SourcePath}') not found/encodable; pass will sample nothing.");
                    effects.Add(key, outPath, packed ?? Array.Empty<byte>());
                }
                effectPaths.Add(outPath);
            }

            var posWoW = mesh.Positions.Select(CoordinateContract.MeshToWoW).ToArray();
            var nrmWoW = mesh.Normals.Select(CoordinateContract.MeshNormalToWoW).ToArray();
            byte[] outM2 = M2VariableTopologyBuilder.Build(donor, posWoW, nrmWoW, mesh.Uv0, mesh,
                viewCount: 4, material: mesh.Material, effectTexturePaths: effectPaths.Count > 0 ? effectPaths : null);
            outM2 = M2GeometryPatcher.RewriteInternalName(outM2, internalName);
            if (motionPlan.Any)
            {
                try
                {
                    // Override the emitter colour with the operator's chosen glow colour (the in-game flame
                    // colour lives in the M2 emitter colour track, not the sprite BLP; ColorRamp=null so
                    // ColorRgb wins). Null glow keeps the source's own colour.
                    var grafts = glowColor is Vector3 gc
                        ? motionPlan.Grafts.Select(g => g with { ColorRgb = gc, ColorRamp = null }).ToList()
                        : motionPlan.Grafts.ToList();
                    // Glow intensity: additive particles render colour AS brightness, so scaling the
                    // colour keys dims or boosts the glow without touching the emission behaviour.
                    // Dimming also shrinks the particles a little (sqrt) so a weak glow reads as
                    // smaller embers rather than gray fire; boosting past 100% only brightens.
                    if (Math.Abs(glowIntensity - 1f) > 0.01f)
                    {
                        float gi = Math.Clamp(glowIntensity, 0.05f, 3f);
                        float sizeMul = gi < 1f ? MathF.Sqrt(gi) : 1f;
                        Vector3 Scaled(Vector3 c) => new(
                            Math.Clamp(c.X * gi, 0f, 255f), Math.Clamp(c.Y * gi, 0f, 255f), Math.Clamp(c.Z * gi, 0f, 255f));
                        grafts = grafts.Select(g => g with
                        {
                            ColorRgb = g.ColorRgb is { } c ? Scaled(c) : g.ColorRgb,
                            ColorRamp = g.ColorRamp is { } ramp
                                ? new M2EmitterColorRamp(Scaled(ramp.Start), Scaled(ramp.Mid), Scaled(ramp.End))
                                : g.ColorRamp,
                            Scale = g.Scale is { } s ? s * sizeMul : g.Scale,
                        }).ToList();
                        int uncolored = grafts.Count(g => g.ColorRgb is null && g.ColorRamp is null);
                        diag.Info("motion.glow.intensity", $"{what}: glow intensity {gi:P0} applied to emitter colour"
                            + (sizeMul < 1f ? " and size" : "")
                            + (uncolored > 0 ? $"; {uncolored} emitter(s) keep donor colour keys (no source colour track), only their size changed" : "") + ".");
                    }
                    var motion = M2EmitterTransplanter.Apply(outM2, grafts);
                    foreach (var note in motion.Notes) diag.Info("motion.emitter", note);
                    if (motion.Grafted > 0)
                    {
                        outM2 = motion.M2;
                        diag.Info("motion.invented",
                            $"{what}: {motion.Grafted} animated particle emitter(s) rebuilt from stock 1.12 donors — an invented conversion: " +
                            "1.12 cannot host the source emitter graph, so position, colour and size were rebuilt on Blizzard's own emission behaviour.");
                    }
                }
                catch (Exception mex) { diag.Warn("motion.failed", $"{what}: emitter graft skipped ({mex.Message}); the piece is unaffected."); }
            }
            // Motion, part two: a glow baked in as an additive PASS (rather than an emitter) still
            // sits dead. 1.12 animates exactly this with a colour track on a global sequence — the
            // same thing Sparkle_A does — so give every additive pass a slow breath.
            try
            {
                var parsedForPulse = M2Reader.Parse(outM2);
                if (parsedForPulse is not null)
                {
                    var glowColors = M2GlowPulseWriter.AdditiveColorIndices(parsedForPulse);
                    if (glowColors.Count > 0)
                    {
                        var pulse = M2GlowPulseWriter.Apply(outM2, glowColors);
                        if (pulse.Pulsed > 0)
                        {
                            outM2 = pulse.M2;
                            foreach (var note in pulse.Notes) diag.Info("motion.pulse", note);
                        }
                    }
                }
            }
            catch (Exception pex) { diag.Warn("motion.pulse.failed", $"Glow pulse skipped: {pex.Message}"); }

            var v = M2BinaryValidator.Validate(outM2, expectedVertexCount: mesh.VertexCount, expectedViews: 4);
            diag.AddRange(v);
            if (v.HasErrors) { diag.Error("import.emit.invalid", $"{what}: emitted M2 failed validation."); return null; }
            return outM2;
        }
        catch (Exception ex)
        {
            diag.Error("import.emit", $"{what}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Whether this piece's skin alpha channel is a reflection mask that 1.12's character frame
    /// would render as transparency (so the mask layers get dropped and the skin flattened), or is
    /// genuinely needed by an alpha-keyed / alpha-blended / add-alpha pass in ANY variant (kept as is).
    /// Probes with the same extractor Emit uses, so the decision and the emitted passes cannot drift.</summary>
    private bool DecideSkinAlphaPolicy(ArmorImportSource src, IEnumerable<M2Model> models, ForgeDiagnostics diag, string what)
    {
        if (!ImportedSkinAlphaPolicy.BlpHasAlphaChannel(src.TextureBlp)) return false; // opaque skin: nothing to do
        bool anyMask = false;
        foreach (var m2 in models)
        {
            LegacyExtractResult? probe;
            try { probe = LegacyWeaponMeshExtractor.Extract(m2, new ForgeDiagnostics("skin-alpha-probe"), Label, bakeEmitters: false); }
            catch { probe = null; }
            if (probe?.Mesh.Passes is not { Count: > 0 } passes) continue;
            var policy = ImportedSkinAlphaPolicy.Apply(passes);
            if (policy.SkinAlphaRequired)
            {
                diag.Info("import.skin.alpha.kept",
                    $"{what}: the skin's alpha channel is sampled by an alpha-keyed/blended pass and is kept as authored " +
                    "(the 1.12 character frame may show it as transparency).");
                return false;
            }
            anyMask |= policy.StrippedMaskPasses > 0;
        }
        if (!anyMask)
            diag.Info("import.skin.alpha.unused", $"{what}: the skin carries an alpha channel no pass depends on; it is flattened to opaque for the 1.12 character frame.");
        return true;
    }

    /// <summary>Re-encode the packaged skin with every texel fully opaque (DXT1, the vanilla format for
    /// opaque skins). Any later recolor bake re-encodes from these pixels and stays opaque.</summary>
    private static void FlattenSkinAlpha(ArmorImportSource src, ForgeDiagnostics diag, string what)
    {
        if (src.TextureBlp is not { Length: > 0 } blp) return;
        try
        {
            var pixels = BlpDecoder.GetPixels(blp, 0, out int w, out int h);
            if (w <= 0 || h <= 0 || pixels.Length < w * h * 4)
            { diag.Warn("import.skin.alpha.flatten", $"{what}: skin could not be decoded; its alpha channel is kept."); return; }
            int translucent = 0;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 255) translucent++;
                pixels[i] = 255;
            }
            if (translucent == 0) return; // header said alpha, but every texel is already solid
            using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmp.GetPixels(), pixels.Length);
            bmp.NotifyPixelsChanged();
            var re = new BlpWriterService().EncodeBitmapToBlp(bmp, useDxt1: true);
            if (re is null)
            { diag.Warn("import.skin.alpha.flatten", $"{what}: opaque DXT1 re-encode failed ({w}×{h}); the skin's alpha channel is kept."); return; }
            src.TextureBlp = re;
            diag.Info("import.skin.alpha.flattened",
                $"{what}: skin alpha channel flattened to opaque ({translucent * 100L / (w * h)}% of texels were translucent) so the 1.12 character frame draws the piece solid.");
        }
        catch (Exception ex)
        {
            diag.Warn("import.skin.alpha.flatten", $"{what}: skin flatten failed ({ex.Message}); its alpha channel is kept.");
        }
    }

    private static string StripStem(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return "";
        int slash = modelName.LastIndexOfAny(['\\', '/']);
        string file = slash >= 0 ? modelName[(slash + 1)..] : modelName;
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }
}
/// <summary>One body-atlas component BLP to pack (painted pieces).</summary>
public sealed class ArmorComponentBlob
{
    public required int Slot { get; init; }
    /// <summary>"_M" / "_F" / "_U" — the gender suffix the client resolves.</summary>
    public required string GenderSuffix { get; init; }
    public required string MpqPath { get; init; }
    public required byte[] Blp { get; init; }
}

/// <summary>A fully resolved import, ready for <c>CustomArmorBuildService.BuildAsync</c>.</summary>
public sealed class ArmorImportSource
{
    public required uint Entry { get; init; }
    public required string Name { get; init; }
    public required string FamilyKey { get; init; }
    public required ArmorRenderKind RenderKind { get; init; }
    public required ArmorMaterial Material { get; init; }
    public int Quality { get; init; }
    public int ItemLevel { get; init; }
    public int RequiredLevel { get; init; }
    public string IconStem { get; init; } = "";
    public int[] GeosetGroup { get; init; } = new int[3];
    public uint GroupSoundIndex { get; init; }
    public uint HelmetVis0 { get; init; }
    public uint HelmetVis1 { get; init; }
    public uint SetId { get; init; }
    public string? SetName { get; init; }

    // Painted
    public List<ArmorComponentBlob> Components { get; } = new();
    // Modelled / cloak
    public string? ModelName { get; set; }
    public string? ModelName2 { get; set; }
    public string? TextureName { get; set; }
    public string? TextureMpqPath { get; set; }
    public byte[]? TextureBlp { get; set; }
    /// <summary>Emitted M2 variants + effect BLPs (modelled lanes).</summary>
    public List<MpqMember> ModelMembers { get; } = new();
}
