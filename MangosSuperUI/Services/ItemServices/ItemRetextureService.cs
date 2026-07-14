using Dapper;
using MangosSuperUI.Models;
using SkiaSharp;
using System.Text;
using System.Text.Json;

namespace MangosSuperUI.Services;

/// <summary>
/// AI-powered item retexturing pipeline with persistent storage.
///
/// All retextures are saved to the `custom_item_retexture` table in vmangos_admin.
/// patch-4.MPQ is rebuilt from ALL stored retextures every time, ensuring
/// the patch always contains every custom item texture (same pattern as
/// SpellCreator's unified patch rebuild).
///
/// Pipeline:
///   1. Ollama crafts a Flux prompt from user's style direction
///   2. ComfyUI (Flux) generates replacement texture (txt2img or img2img)
///   3. Result resized to vanilla dimensions, encoded to BLP
///   4. M2 cloned with patched texture path
///   5. Everything saved to DB (BLP + M2 as BLOBs)
///   6. RebuildPatchM() reads ALL retextures from DB, builds unified patch-4.MPQ
///      with all custom BLPs, M2s, and a single patched ItemDisplayInfo.dbc
///   7. patch-4.MPQ deployed to WoW client Data folder
/// </summary>
public class ItemRetextureService
{
    private readonly ComfyUIDispatcher _comfy;
    private readonly MpqReaderService _mpq;
    private readonly BlpWriterService _blpWriter;
    private readonly ItemTextureService _itemTextures;
    private readonly DbcService _dbc;
    private readonly ConnectionFactory _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<ItemRetextureService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _http;

    private readonly string _dbcPath;
    private readonly string _ollamaUrl;
    private readonly string _ollamaModel;
    private readonly string _clipModel2;

    private static readonly TimeSpan OllamaTimeout = TimeSpan.FromSeconds(20);

    public const uint CUSTOM_DISPLAY_BASE = 60000;

    public ItemRetextureService(
        ComfyUIDispatcher comfy,
        MpqReaderService mpq,
        BlpWriterService blpWriter,
        ItemTextureService itemTextures,
        DbcService dbc,
        ConnectionFactory db,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<ItemRetextureService> logger,
        ILoggerFactory loggerFactory)
    {
        _comfy = comfy;
        _mpq = mpq;
        _blpWriter = blpWriter;
        _itemTextures = itemTextures;
        _dbc = dbc;
        _db = db;
        _env = env;
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };

        _dbcPath = config["Vmangos:DbcPath"]
            ?? "/home/wowvmangos/vmangos/run/data/5875/dbc";

        var ollamaBase = (config["SpellCreator:Ollama:BaseUrl"] ?? "").TrimEnd('/');
        _ollamaUrl = string.IsNullOrEmpty(ollamaBase) ? "" : $"{ollamaBase}/api/generate";
        _ollamaModel = config["SpellCreator:Ollama:Model"] ?? "";
        _clipModel2 = config["SpellCreator:ComfyUI:ClipModel2"] ?? "";
    }

    // ═══════════════════════════════════════════════════════════════════
    // DB SCHEMA INIT
    // ═══════════════════════════════════════════════════════════════════

    public async Task EnsureTableAsync()
    {
        using var conn = _db.Admin();
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS custom_item_retexture (
                id INT AUTO_INCREMENT PRIMARY KEY,
                display_id INT UNSIGNED NOT NULL,
                new_display_id INT UNSIGNED NOT NULL,
                item_name VARCHAR(255) NOT NULL DEFAULT '',
                texture_filename VARCHAR(255) NOT NULL DEFAULT '',
                texture_mpq_path VARCHAR(512) NOT NULL DEFAULT '',
                custom_blp_mpq_path VARCHAR(512) NOT NULL DEFAULT '',
                custom_m2_mpq_path VARCHAR(512) NOT NULL DEFAULT '',
                custom_blp LONGBLOB,
                custom_m2 LONGBLOB,
                prompt TEXT,
                style_direction VARCHAR(512) NOT NULL DEFAULT '',
                created_at DATETIME DEFAULT NOW()
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Body-atlas (painted armor) retextures. Unlike weapons (one model
        // texture → one BLP → DBC field 3), painted armor has up to 8 component
        // textures (m_texture[0..7] = ItemDisplayInfo fields 14..21), each its
        // own BLP in its own Item\TextureComponents\{subdir}\ directory. So this
        // table is ONE ROW PER SLOT, all sharing a new_display_id. RebuildPatchM
        // groups by new_display_id, clones the source row once, and patches each
        // slot's field. Separate table so the single-BLP weapon flow is untouched.
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS custom_item_retexture_atlas (
                id INT AUTO_INCREMENT PRIMARY KEY,
                display_id INT UNSIGNED NOT NULL,
                new_display_id INT UNSIGNED NOT NULL,
                item_name VARCHAR(255) NOT NULL DEFAULT '',
                slot TINYINT UNSIGNED NOT NULL,
                custom_blp_mpq_path VARCHAR(512) NOT NULL DEFAULT '',
                custom_blp LONGBLOB,
                style_direction VARCHAR(512) NOT NULL DEFAULT '',
                created_at DATETIME DEFAULT NOW(),
                INDEX idx_atlas_newdisplay (new_display_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC API — GENERATE RETEXTURE
    // ═══════════════════════════════════════════════════════════════════

    public async Task<RetextureResult> RetextureAsync(RetextureRequest request, CancellationToken ct = default)
    {
        await EnsureTableAsync();
        var result = new RetextureResult { DisplayId = request.DisplayId };

        try
        {
            _logger.LogInformation(
                "Retexture: Starting for displayId {Id}, texture '{Tex}', style: {Style}, mode: {Mode}",
                request.DisplayId, request.OriginalBlpFilename, request.StyleDirection,
                request.ModifyExisting ? "img2img" : "txt2img");

            // ── Step 1: Read original texture metadata ──
            var texInfo = _itemTextures.GetTexturesForDisplay(request.DisplayId);
            if (texInfo == null)
            {
                result.Error = "Could not extract textures for this item";
                return result;
            }

            var targetTex = texInfo.Textures.FirstOrDefault(t =>
                t.MpqPath.Equals(request.OriginalMpqPath, StringComparison.OrdinalIgnoreCase)
                || t.Filename.Equals(request.OriginalBlpFilename, StringComparison.OrdinalIgnoreCase));

            if (targetTex == null)
            {
                targetTex = texInfo.Textures.FirstOrDefault();
                if (targetTex == null)
                {
                    result.Error = "No textures found for this model";
                    return result;
                }
            }

            int targetW = targetTex.Width > 0 ? targetTex.Width : 64;
            int targetH = targetTex.Height > 0 ? targetTex.Height : 64;
            bool useDxt1 = targetTex.Format == "DXT1";
            result.OriginalWidth = targetW;
            result.OriginalHeight = targetH;
            result.OriginalFormat = targetTex.Format;

            // ── Step 2: Craft prompt via Ollama ──
            string prompt;
            if (!string.IsNullOrEmpty(request.CustomPrompt))
            {
                prompt = request.CustomPrompt;
            }
            else
            {
                prompt = await CraftTexturePromptAsync(
                    request.ItemName, request.StyleDirection, targetTex.Filename,
                    targetW, targetH, ct);
            }
            result.Prompt = prompt;
            _logger.LogInformation("Retexture: Prompt → \"{Prompt}\"", prompt);

            // ── Step 3: Generate with Flux via ComfyUI ──
            if (!await _comfy.IsAnyNodeOnlineAsync(ct))
            {
                result.Error = "No ComfyUI nodes available";
                return result;
            }

            int genW = 512;
            int genH = targetW == targetH ? 512 : (int)(512.0 * targetH / targetW);
            genH = Math.Max(64, (genH / 64) * 64);

            Dictionary<string, object> workflow;

            if (request.ModifyExisting && targetTex.HasPreview)
            {
                string previewPath = Path.Combine(_env.WebRootPath,
                    targetTex.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(previewPath))
                {
                    workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
                }
                else
                {
                    string uploadPngPath = Path.Combine(
                        Path.GetTempPath(),
                        $"retex_src_{request.DisplayId}_{Path.GetFileName(previewPath)}");

                    using (var srcBitmap = SKBitmap.Decode(previewPath))
                    {
                        if (srcBitmap != null)
                        {
                            using var resized = srcBitmap.Resize(
                                new SKImageInfo(genW, genH, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                            if (resized != null)
                            {
                                using var outStream = File.Create(uploadPngPath);
                                resized.Encode(outStream, SKEncodedImageFormat.Png, 100);
                            }
                        }
                    }

                    string? uploadedName = await _comfy.UploadImageFileAsync(uploadPngPath, ct);
                    try { File.Delete(uploadPngPath); } catch { }

                    if (uploadedName == null)
                    {
                        workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
                    }
                    else
                    {
                        float denoise = Math.Clamp(request.DenoiseStrength, 0.1f, 1.0f);
                        workflow = BuildImg2ImgWorkflow(prompt, uploadedName, request.DisplayId,
                            genW, genH, denoise);
                        _logger.LogInformation("Retexture: img2img mode, denoise={Denoise}", denoise);
                    }
                }
            }
            else
            {
                workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
            }

            var outputDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_output");
            string? generatedPng = await _comfy.GenerateAsync(
                workflow, $"retex_{request.DisplayId}", outputDir, ct);

            if (generatedPng == null)
            {
                result.Error = "Flux generation failed or timed out";
                return result;
            }

            // ── Step 4: Resize to vanilla dimensions ──
            var resizedDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_resized");
            Directory.CreateDirectory(resizedDir);
            string resizedPng = Path.Combine(resizedDir,
                $"{request.DisplayId}_{Path.GetFileNameWithoutExtension(targetTex.Filename)}.png");

            using (var resizedBmp = _blpWriter.ResizePngToBitmap(generatedPng, targetW, targetH))
            {
                if (resizedBmp == null)
                {
                    result.Error = "Failed to resize generated texture";
                    return result;
                }
                using var outStream = File.Create(resizedPng);
                resizedBmp.Encode(outStream, SKEncodedImageFormat.Png, 100);
            }

            result.GeneratedPngPath = $"/item_textures_cache/retexture_resized/{Path.GetFileName(resizedPng)}";

            // ── Step 5: Encode to BLP ──
            using var blpBitmap = SKBitmap.Decode(resizedPng);
            if (blpBitmap == null)
            {
                result.Error = "Failed to decode resized PNG for BLP encoding";
                return result;
            }

            var blpBytes = _blpWriter.EncodeBitmapToBlp(blpBitmap, useDxt1);
            if (blpBytes == null)
            {
                result.Error = "BLP encoding failed";
                return result;
            }

            // ── Step 6: Build MPQ paths ──
            // DBC-only approach: no M2 cloning.
            // The vanilla M2 uses type-1 textures resolved via ItemDisplayInfo.dbc,
            // not embedded filenames. We just need:
            //   - Custom BLP in the same directory as the vanilla M2
            //   - DBC TextureName1 pointing at the custom BLP name (no ext, no dir)
            //   - DBC ModelName1 unchanged (vanilla M2 from base MPQs)
            string customBlpName = $"Custom_{request.DisplayId}_{Path.GetFileNameWithoutExtension(targetTex.Filename)}.blp";
            string customBlpMpqPath = customBlpName;

            var modelInfo = texInfo.ModelName;
            if (!string.IsNullOrEmpty(modelInfo))
            {
                string m2Dir = GuessM2Directory(modelInfo);
                customBlpMpqPath = $"{m2Dir}{customBlpName}";
            }

            // ── Step 7: Allocate new displayId and save to DB ──
            uint newDisplayId = await AllocateDisplayIdAsync();

            using (var conn = _db.Admin())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO custom_item_retexture
                        (display_id, new_display_id, item_name, texture_filename, texture_mpq_path,
                         custom_blp_mpq_path, custom_m2_mpq_path, custom_blp, custom_m2,
                         prompt, style_direction)
                    VALUES
                        (@DisplayId, @NewDisplayId, @ItemName, @TexFilename, @TexMpqPath,
                         @BlpMpqPath, '', @BlpBytes, NULL,
                         @Prompt, @Style)",
                    new
                    {
                        DisplayId = request.DisplayId,
                        NewDisplayId = newDisplayId,
                        ItemName = request.ItemName,
                        TexFilename = targetTex.Filename,
                        TexMpqPath = targetTex.MpqPath,
                        BlpMpqPath = customBlpMpqPath,
                        BlpBytes = blpBytes,
                        Prompt = prompt,
                        Style = request.StyleDirection
                    });
            }

            _logger.LogInformation(
                "Retexture: Saved to DB — displayId {Old}→{New}, BLP: {Blp}",
                request.DisplayId, newDisplayId, customBlpMpqPath);

            // Register in DbcService so SuperUI knows about this displayId immediately
            // Pass null for both model and texture — clones everything from source.
            // SuperUI serves custom textures from the DB via TryLoadCustomRetexture,
            // and finds the vanilla M2 via the original model name for GLB generation.
            _dbc.RegisterCustomDisplayEntry(newDisplayId, request.DisplayId, null, null);

            // ── Step 9: Rebuild patch-4.MPQ with ALL retextures ──
            var rebuildResult = await RebuildPatchMAsync();

            result.PatchMpqPath = rebuildResult.PatchWebPath;
            result.CustomBlpMpqPath = customBlpMpqPath;
            result.CustomM2MpqPath = null;
            result.NewDisplayId = newDisplayId;
            result.BlpSizeBytes = blpBytes.Length;
            result.Success = rebuildResult.Success;
            if (!rebuildResult.Success)
                result.Error = rebuildResult.Error;

            _itemTextures.InvalidateCache(request.DisplayId);
            _itemTextures.InvalidateCache(newDisplayId);

            _logger.LogInformation(
                "Retexture: Complete! displayId {Old}→{New}, total retextures in patch: {Count}",
                request.DisplayId, newDisplayId, rebuildResult.TotalEntries);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retexture: Failed for displayId {Id}", request.DisplayId);
            result.Error = ex.Message;
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // PREVIEW-ONLY RENDER (May 2026)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Run the Flux generation portion of RetextureAsync (steps 1–4: craft
    /// prompt, build workflow, ComfyUI generate, resize to vanilla dims) and
    /// return the resized PNG path — WITHOUT touching the DB or rebuilding
    /// patch-4.MPQ. The PNG lives under wwwroot/item_textures_cache/retexture_resized/
    /// with a Guid-suffixed filename so concurrent previews don't overwrite
    /// each other. Used by /Items/PreviewFluxGlb and PreviewPaletteGlb's
    /// optional Flux polish step. Commit happens later via
    /// RetextureFromBitmapAsync on the same PNG, so what was previewed on
    /// the character is byte-identical to what's persisted.
    /// </summary>
    /// <param name="request">Same shape RetextureAsync takes (DisplayId, ItemName,
    /// StyleDirection / CustomPrompt, ModifyExisting, DenoiseStrength, OriginalMpqPath,
    /// OriginalBlpFilename).</param>
    /// <returns>Absolute path to the resized PNG on disk, or null on failure.
    /// On null, check logs — the failure has already been logged.</returns>
    public async Task<string?> RenderToPngAsync(RetextureRequest request, CancellationToken ct = default)
    {
        try
        {
            // ── Step 1: Read original texture metadata ──
            var texInfo = _itemTextures.GetTexturesForDisplay(request.DisplayId);
            if (texInfo == null)
            {
                _logger.LogWarning("RenderToPngAsync: no textures for displayId {Id}",
                    request.DisplayId);
                return null;
            }

            var targetTex = texInfo.Textures.FirstOrDefault(t =>
                t.MpqPath.Equals(request.OriginalMpqPath, StringComparison.OrdinalIgnoreCase)
                || t.Filename.Equals(request.OriginalBlpFilename, StringComparison.OrdinalIgnoreCase))
                ?? texInfo.Textures.FirstOrDefault();
            if (targetTex == null)
            {
                _logger.LogWarning("RenderToPngAsync: no matching texture for displayId {Id}",
                    request.DisplayId);
                return null;
            }

            int targetW = targetTex.Width > 0 ? targetTex.Width : 64;
            int targetH = targetTex.Height > 0 ? targetTex.Height : 64;

            // ── Step 2: Craft prompt via Ollama (skip if CustomPrompt given) ──
            string prompt = !string.IsNullOrEmpty(request.CustomPrompt)
                ? request.CustomPrompt
                : await CraftTexturePromptAsync(
                    request.ItemName, request.StyleDirection, targetTex.Filename,
                    targetW, targetH, ct);
            _logger.LogInformation("RenderToPngAsync: Prompt → \"{Prompt}\"", prompt);

            // ── Step 3: Generate with Flux via ComfyUI ──
            if (!await _comfy.IsAnyNodeOnlineAsync(ct))
            {
                _logger.LogWarning("RenderToPngAsync: no ComfyUI nodes online");
                return null;
            }

            int genW = 512;
            int genH = targetW == targetH ? 512 : (int)(512.0 * targetH / targetW);
            genH = Math.Max(64, (genH / 64) * 64);

            Dictionary<string, object> workflow;

            if (request.ModifyExisting && targetTex.HasPreview)
            {
                string previewPath = Path.Combine(_env.WebRootPath,
                    targetTex.PreviewPngPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(previewPath))
                {
                    workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
                }
                else
                {
                    string uploadPngPath = Path.Combine(
                        Path.GetTempPath(),
                        $"retex_src_{request.DisplayId}_{Path.GetFileName(previewPath)}");

                    using (var srcBitmap = SKBitmap.Decode(previewPath))
                    {
                        if (srcBitmap != null)
                        {
                            using var resized = srcBitmap.Resize(
                                new SKImageInfo(genW, genH, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                            if (resized != null)
                            {
                                using var outStream = File.Create(uploadPngPath);
                                resized.Encode(outStream, SKEncodedImageFormat.Png, 100);
                            }
                        }
                    }

                    string? uploadedName = await _comfy.UploadImageFileAsync(uploadPngPath, ct);
                    try { File.Delete(uploadPngPath); } catch { }

                    if (uploadedName == null)
                    {
                        workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
                    }
                    else
                    {
                        float denoise = Math.Clamp(request.DenoiseStrength, 0.1f, 1.0f);
                        workflow = BuildImg2ImgWorkflow(prompt, uploadedName, request.DisplayId,
                            genW, genH, denoise);
                        _logger.LogInformation("RenderToPngAsync: img2img mode, denoise={Denoise}", denoise);
                    }
                }
            }
            else
            {
                workflow = BuildTextureWorkflow(prompt, request.DisplayId, genW, genH);
            }

            var outputDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_output");
            string? generatedPng = await _comfy.GenerateAsync(
                workflow, $"retex_{request.DisplayId}", outputDir, ct);

            if (generatedPng == null)
            {
                _logger.LogWarning("RenderToPngAsync: Flux generation failed for displayId {Id}",
                    request.DisplayId);
                return null;
            }

            // ── Step 4: Resize to vanilla dimensions ──
            // Guid suffix so concurrent previews / preview+commit don't trash
            // each other. RetextureAsync uses a deterministic filename because
            // it commits immediately and discards the PNG; here the PNG IS
            // the staged artifact, so uniqueness matters.
            var resizedDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_resized");
            Directory.CreateDirectory(resizedDir);
            string resizedPng = Path.Combine(resizedDir,
                $"preview_{request.DisplayId}_{Path.GetFileNameWithoutExtension(targetTex.Filename)}_{Guid.NewGuid():N}.png");

            using (var resizedBmp = _blpWriter.ResizePngToBitmap(generatedPng, targetW, targetH))
            {
                if (resizedBmp == null)
                {
                    _logger.LogWarning("RenderToPngAsync: failed to resize generated texture");
                    return null;
                }
                using var outStream = File.Create(resizedPng);
                resizedBmp.Encode(outStream, SKEncodedImageFormat.Png, 100);
            }

            return resizedPng;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenderToPngAsync: failed for displayId {Id}",
                request.DisplayId);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // REBUILD PATCH-M.MPQ — ALL RETEXTURES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retexture from an already-rendered PNG (e.g. from palette-swap vision recolor).
    /// Skips Ollama prompt crafting and ComfyUI/Flux entirely — just resizes the PNG
    /// to vanilla dimensions, encodes BLP, saves to DB, rebuilds patch-4.MPQ.
    ///
    /// Used by the palette-swap-only path so the texture isn't degraded by a needless
    /// Flux img2img passthrough (which softens detail via the VAE roundtrip and burns
    /// VRAM/time loading Flux for no reason).
    /// </summary>
    /// <param name="rebuildPatch">
    /// When false, commit to the DB and skip the patch-4.MPQ rebuild.
    ///
    /// RebuildPatchMAsync regenerates the WHOLE archive from EVERY row in both
    /// retexture tables — it reads ItemDisplayInfo.dbc off disk, pulls every BLP
    /// BLOB out of the DB, repacks, writes, and redeploys to the client. That is
    /// exactly right for an interactive one-off commit (the user gets a usable
    /// patch immediately) and quadratic for a batch: job N pays for jobs 1..N-1
    /// all over again. A few hundred lootifier jobs turn a minutes-long run into
    /// an hours-long one.
    ///
    /// The batch caller passes false and rebuilds ONCE when the queue drains.
    /// Nothing is lost by deferring: the DB rows are the durable artifact and the
    /// patch is a pure function of them. RegisterCustomDisplayEntry still runs per
    /// commit, so SuperUI previews the new displayId immediately either way.
    /// </param>
    /// <param name="preResolved">
    /// The texture entry the CALLER already resolved, if it has one.
    ///
    /// Without this, the method re-resolved from scratch via
    /// GetTexturesForDisplay(request.DisplayId) — which parses the M2 and returns
    /// null whenever there isn't one. Cloaks have no M2 (they're a character
    /// geoset), so the queue would resolve a cape correctly with GetCapeTexture,
    /// burn a full palette recolor on it, hand it here... and then this method
    /// would throw the entry away, re-resolve through the M2-only path, get null,
    /// and fail with "Could not extract textures for this item". 230 cloak jobs
    /// died that way, every one of them after doing the expensive work.
    ///
    /// Helms resolved through GetObjectComponentTexture hit exactly the same wall.
    ///
    /// When supplied, this entry is used as-is and the re-resolve is skipped.
    /// </param>
    public async Task<RetextureResult> RetextureFromBitmapAsync(
        RetextureRequest request, string sourcePngPath, CancellationToken ct = default,
        bool rebuildPatch = true, ItemTextureEntry? preResolved = null)
    {
        await EnsureTableAsync();
        var result = new RetextureResult { DisplayId = request.DisplayId };

        try
        {
            if (!File.Exists(sourcePngPath))
            {
                result.Error = $"Source PNG not found: {sourcePngPath}";
                return result;
            }

            // ── Step 1: Read original texture metadata ──
            // Trust the caller's entry when it gave us one — it may have come from
            // a resolver this method cannot reproduce (cape / object-component),
            // and re-deriving it here is what killed every cloak and helm job.
            ItemTextureEntry? targetTex = preResolved;

            // Stays null on the pre-resolved path — a cape / object-component entry
            // has no M2, so there is no ItemTextureInfo to accompany it. Step 5 must
            // not assume this is non-null.
            ItemTextureInfo? texInfo = null;

            if (targetTex == null)
            {
                texInfo = _itemTextures.GetTexturesForDisplay(request.DisplayId);
                if (texInfo == null)
                {
                    result.Error = "Could not extract textures for this item";
                    return result;
                }

                targetTex = texInfo.Textures.FirstOrDefault(t =>
                    t.MpqPath.Equals(request.OriginalMpqPath, StringComparison.OrdinalIgnoreCase)
                    || t.Filename.Equals(request.OriginalBlpFilename, StringComparison.OrdinalIgnoreCase))
                    ?? texInfo.Textures.FirstOrDefault();

                if (targetTex == null)
                {
                    result.Error = "No textures found for this model";
                    return result;
                }
            }

            int targetW = targetTex.Width > 0 ? targetTex.Width : 64;
            int targetH = targetTex.Height > 0 ? targetTex.Height : 64;
            bool useDxt1 = targetTex.Format == "DXT1";
            result.OriginalWidth = targetW;
            result.OriginalHeight = targetH;
            result.OriginalFormat = targetTex.Format;

            _logger.LogInformation(
                "RetextureFromBitmap: displayId {Id}, source '{Src}' → {W}x{H} {Fmt} (no Flux)",
                request.DisplayId, Path.GetFileName(sourcePngPath), targetW, targetH, targetTex.Format);

            // ── Step 2: Resize source PNG to vanilla dimensions ──
            var resizedDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "retexture_resized");
            Directory.CreateDirectory(resizedDir);
            string resizedPng = Path.Combine(resizedDir,
                $"{request.DisplayId}_palette_{Path.GetFileNameWithoutExtension(targetTex.Filename)}.png");

            using (var resizedBmp = _blpWriter.ResizePngToBitmap(sourcePngPath, targetW, targetH))
            {
                if (resizedBmp == null)
                {
                    result.Error = "Failed to resize recolored texture";
                    return result;
                }
                using var outStream = File.Create(resizedPng);
                resizedBmp.Encode(outStream, SKEncodedImageFormat.Png, 100);
            }

            result.GeneratedPngPath = $"/item_textures_cache/retexture_resized/{Path.GetFileName(resizedPng)}";

            // ── Step 3: Encode to BLP ──
            using var blpBitmap = SKBitmap.Decode(resizedPng);
            if (blpBitmap == null)
            {
                result.Error = "Failed to decode resized PNG for BLP encoding";
                return result;
            }

            var blpBytes = _blpWriter.EncodeBitmapToBlp(blpBitmap, useDxt1);
            if (blpBytes == null)
            {
                result.Error = "BLP encoding failed";
                return result;
            }

            // ── Step 4: Allocate new displayId BEFORE building MPQ paths ──
            // Use newDisplayId in BLP filename so each retexture of the same
            // source item gets a unique BLP path (fixes filename-collision bug).
            uint newDisplayId = await AllocateDisplayIdAsync();

            // ── Step 5: Build MPQ paths ──
            //
            // TWO hard-won rules here, both from the black-sword incident
            // (Haggard's Sword of Glory, display 61844):
            //
            // 1. targetTex.Filename is NOT always a bare filename. Some M2 texture
            //    entries carry a FULL PATH (ITEM\OBJECTCOMPONENTS\WEAPON\
            //    ARMORREFLECT3.BLP), and on Linux Path.GetFileNameWithoutExtension
            //    does not split on backslashes — so the whole path survived inside
            //    the "filename" and the directory got prepended AGAIN:
            //
            //      ITEM\OBJECTCOMPONENTS\WEAPON\Custom_61844_ITEM\OBJECTCOMPONENTS\WEAPON\ARMORREFLECT3.blp
            //
            //    A directory inside a filename. The client derives
            //    <dir of ModelName1>\<TextureName1>.blp, never finds that, and the
            //    weapon renders BLACK. Normalize separators BEFORE taking the stem.
            //
            // 2. The DIRECTORY is never guessed when the source texture's own path
            //    is known. The custom BLP must live where the source lived —
            //    that is by definition the directory the model loads from.
            //    GuessM2Directory keyword-matching is the last resort only.
            string stem = Path.GetFileNameWithoutExtension(
                (targetTex.Filename ?? "").Replace('\\', '/'));
            string customBlpName = $"Custom_{newDisplayId}_{stem}.blp";
            string customBlpMpqPath = customBlpName;

            string srcMpqPath = (targetTex.MpqPath ?? "").Replace('/', '\\');
            int cut = srcMpqPath.LastIndexOf('\\');
            if (cut >= 0)
            {
                // Source texture's own directory — authoritative.
                customBlpMpqPath = srcMpqPath.Substring(0, cut + 1) + customBlpName;
            }
            else if (!string.IsNullOrEmpty(texInfo?.ModelName))
            {
                // No source path known — fall back to guessing from the model name.
                string m2Dir = GuessM2Directory(texInfo.ModelName);
                customBlpMpqPath = $"{m2Dir}{customBlpName}";
            }

            // ── Step 6: Save to DB ──
            using (var conn = _db.Admin())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO custom_item_retexture
                        (display_id, new_display_id, item_name, texture_filename, texture_mpq_path,
                         custom_blp_mpq_path, custom_m2_mpq_path, custom_blp, custom_m2,
                         prompt, style_direction)
                    VALUES
                        (@DisplayId, @NewDisplayId, @ItemName, @TexFilename, @TexMpqPath,
                         @BlpMpqPath, '', @BlpBytes, NULL,
                         @Prompt, @Style)",
                    new
                    {
                        DisplayId = request.DisplayId,
                        NewDisplayId = newDisplayId,
                        ItemName = request.ItemName,
                        TexFilename = targetTex.Filename,
                        TexMpqPath = targetTex.MpqPath,
                        BlpMpqPath = customBlpMpqPath,
                        BlpBytes = blpBytes,
                        Prompt = "[palette swap — no AI prompt]",
                        Style = request.StyleDirection
                    });
            }

            _logger.LogInformation(
                "RetextureFromBitmap: Saved to DB — displayId {Old}→{New}, BLP: {Blp}",
                request.DisplayId, newDisplayId, customBlpMpqPath);

            _dbc.RegisterCustomDisplayEntry(newDisplayId, request.DisplayId, null, null);

            // ── Step 7: Rebuild patch-4.MPQ ──
            // Deferred in batch mode — see the rebuildPatch param doc. The DB
            // rows above are the durable artifact; the patch is derived from them.
            PatchMRebuildResult rebuildResult = rebuildPatch
                ? await RebuildPatchMAsync()
                : new PatchMRebuildResult { Success = true };

            result.PatchMpqPath = rebuildResult.PatchWebPath;
            result.CustomBlpMpqPath = customBlpMpqPath;
            result.CustomM2MpqPath = null;
            result.NewDisplayId = newDisplayId;
            result.BlpSizeBytes = blpBytes.Length;
            result.Success = rebuildResult.Success;
            result.Prompt = "[palette swap]";
            if (!rebuildResult.Success)
                result.Error = rebuildResult.Error;

            _itemTextures.InvalidateCache(request.DisplayId);
            _itemTextures.InvalidateCache(newDisplayId);

            _logger.LogInformation(
                "RetextureFromBitmap: Complete! displayId {Old}→{New}, total retextures in patch: {Count}",
                request.DisplayId, newDisplayId, rebuildResult.TotalEntries);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetextureFromBitmap: Failed for displayId {Id}", request.DisplayId);
            result.Error = ex.Message;
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // BODY-ATLAS COMMIT (painted armor — May 2026)
    // ═══════════════════════════════════════════════════════════════════

    // Slot → MPQ subdir under Item\TextureComponents\. Canonical vanilla 1.12
    // layout (same map as BodyAtlasTextureService.SlotToSubdir — kept inline so
    // the services stay decoupled). The client prepends this dir and appends
    // .blp to the bare name we write into the m_texture[] DBC field.
    private static readonly Dictionary<int, string> AtlasSlotSubdir = new()
    {
        { 0, "ArmUpperTexture" },
        { 1, "ArmLowerTexture" },
        { 2, "HandTexture" },
        { 3, "TorsoUpperTexture" },
        { 4, "TorsoLowerTexture" },
        { 5, "LegUpperTexture" },
        { 6, "LegLowerTexture" },
        { 7, "FootTexture" },
    };

    /// <summary>
    /// Gender suffixes packed for each component BLP. ONE entry, deliberately.
    ///
    /// Every file here multiplies the archive's file count by the number of atlas
    /// rows, and War3Net caps HashTableSize at ushort — so the largest MPQ hash
    /// table is 32768 and the whole archive can hold ~32k files, full stop. At
    /// 3 suffixes a full quest+crafted run needs ~34,700 files and CANNOT BE BUILT.
    ///
    /// "_U" alone is correct AND sufficient. The client resolves component textures
    /// as {name}_{M|F|U}.blp, and it falls back to _U when no gendered file exists —
    /// proven by vanilla data: BodyAtlasTextureService probes _M → _F → _U and
    /// breaks on first hit, and it reports _U hits for items like
    /// Robe_A_01Indigo_Pant_LU (→ ..._LU_U.blp). Those robes have no _M or _F file
    /// at all, and they render correctly on male and female characters in-game.
    /// So the client reads _U for both.
    ///
    /// It is also honest: we recolor ONE source PNG per slot. The texture genuinely
    /// is unisex — shipping it three times under three names was duplicating the
    /// same bytes to satisfy a suffix rule we had already satisfied.
    ///
    /// IF a retextured piece renders as missing/stale hands in-game after this,
    /// the _U fallback theory is wrong: add "_M", "_F" here and re-run the patch
    /// rebuild. Watch the MpqBuilder log line for the resulting hash-table fill.
    /// </summary>
    private static readonly string[] COMPONENT_SUFFIXES = { "_U" };

    /// <summary>
    /// Pack a body-atlas component BLP under EVERY gender suffix the client may
    /// ask for.
    ///
    /// This is the single most important quirk of painted armor. The client does
    /// NOT open the filename we store. It takes the bare name out of
    /// ItemDisplayInfo m_texture[slot], prepends Item\TextureComponents\{subdir}\,
    /// and appends a gender suffix:
    ///
    ///     m_texture[2] = "Custom_61526_s2_Gloves"
    ///       → Item\TextureComponents\HandTexture\Custom_61526_s2_Gloves_M.blp
    ///       → ..._F.blp  (female)   ..._U.blp  (unisex components)
    ///
    /// Every vanilla component BLP is suffixed this way (confirmed by the MPQ
    /// probe in ItemsController). Packing only "Custom_..._Gloves.blp" means the
    /// client requests a file that isn't in the archive — and because the cloned
    /// DBC row DID load, it doesn't fall back to vanilla: it paints nothing, and
    /// the armor piece goes untextured in-game. That was the "patch-4 loads but
    /// only breaks the texture" bug.
    ///
    /// We emit the same bytes under _M / _F / _U plus the bare name, so whichever
    /// path the client resolves, it hits. (Recoloring the male source and serving
    /// it as female is a known approximation — see the gender note in
    /// CommitBodyAtlasAsync.)
    /// </summary>
    private static void AddComponentBlpAllGenders(
        MpqBuilderService mpqBuilder, string blpMpqPath, byte[] blp)
    {
        int cut = blpMpqPath.LastIndexOf('\\');
        string dir = cut >= 0 ? blpMpqPath.Substring(0, cut + 1) : "";
        string file = blpMpqPath.Substring(cut + 1);
        string bare = file.EndsWith(".blp", StringComparison.OrdinalIgnoreCase)
            ? file.Substring(0, file.Length - 4)
            : file;

        foreach (var sfx in COMPONENT_SUFFIXES)
            mpqBuilder.AddFile($@"{dir}{bare}{sfx}.blp", blp);
    }

    /// <summary>
    /// Commit a body-atlas (painted armor) retexture. The painted-armor analog
    /// of RetextureFromBitmapAsync: instead of one model texture → one BLP →
    /// DBC field 3, this takes the recolored COMPONENT PNGs (one per body-atlas
    /// slot) and persists each as its own BLP, all under a single new displayId,
    /// patched into m_texture[0..7] (DBC fields 14..21) on rebuild.
    ///
    /// Each slot's BLP goes in its own Item\TextureComponents\{subdir}\ dir with
    /// a displayId-unique filename (Custom_{newId}_s{slot}_{base}.blp) — unique
    /// per slot AND per displayId so MPQ entries never collide (the bug where
    /// every retexture shared a source-derived BLP name).
    ///
    /// slotPngPaths: slot index (0..7) → absolute on-disk recolored PNG path
    /// (already validated by the caller as living under item_textures_cache).
    /// </summary>
    /// <param name="rebuildPatch">
    /// When false, commit to the DB and skip the patch-4.MPQ rebuild.
    ///
    /// RebuildPatchMAsync regenerates the WHOLE archive from EVERY row in both
    /// retexture tables — it reads ItemDisplayInfo.dbc off disk, pulls every BLP
    /// BLOB out of the DB, repacks, writes, and redeploys to the client. That is
    /// exactly right for an interactive one-off commit (the user gets a usable
    /// patch immediately) and quadratic for a batch: job N pays for jobs 1..N-1
    /// all over again. A few hundred lootifier jobs turn a minutes-long run into
    /// an hours-long one.
    ///
    /// The batch caller passes false and rebuilds ONCE when the queue drains.
    /// Nothing is lost by deferring: the DB rows are the durable artifact and the
    /// patch is a pure function of them. RegisterCustomDisplayEntry still runs per
    /// commit, so SuperUI previews the new displayId immediately either way.
    /// </param>
    public async Task<RetextureResult> CommitBodyAtlasAsync(
        uint sourceDisplayId, string itemName, string styleDirection,
        Dictionary<int, string> slotPngPaths, CancellationToken ct = default,
        bool rebuildPatch = true)
    {
        await EnsureTableAsync();
        var result = new RetextureResult { DisplayId = sourceDisplayId };

        try
        {
            if (slotPngPaths == null || slotPngPaths.Count == 0)
            {
                result.Error = "No slot PNGs supplied";
                return result;
            }

            // Encode each slot PNG → BLP first, so a single failure aborts before
            // we allocate a displayId or write any rows. Body-atlas component
            // textures are UNCOMPRESSED/PALETTIZED — vanilla stores them as plain
            // BLP, so they go through EncodeBitmapToBlpUncompressed, never the DXT
            // encoders.
            var encoded = new List<(int Slot, byte[] Blp)>();
            foreach (var kv in slotPngPaths.OrderBy(k => k.Key))
            {
                int slot = kv.Key;
                string png = kv.Value;
                if (!AtlasSlotSubdir.ContainsKey(slot))
                {
                    _logger.LogWarning("CommitBodyAtlas: skipping invalid slot {Slot}", slot);
                    continue;
                }
                if (!File.Exists(png))
                {
                    _logger.LogWarning("CommitBodyAtlas: slot {Slot} PNG missing: {Png}", slot, png);
                    continue;
                }

                // Component textures keep their own native dimensions — re-encode
                // straight from the recolored PNG (it was decoded from the vanilla
                // component BLP, so it's already the right size). Decode → BLP.
                using var bmp = SKBitmap.Decode(png);
                if (bmp == null)
                {
                    _logger.LogWarning("CommitBodyAtlas: decode failed slot {Slot}", slot);
                    continue;
                }
                // PALETTIZED, not DXT. Body-atlas component textures are CPU-blitted
                // into the shared character atlas by the client, which reads raw
                // indexed pixels — vanilla ships every Item\TextureComponents\ BLP
                // uncompressed. This used to call EncodeBitmapToBlp(bmp, false), and
                // `useDxt1: false` means DXT3, NOT uncompressed: the comment above
                // asserted "vanilla stores them as plain BLP" but no such encoder
                // existed. Every component texture went out as DXT3, which composites
                // to garbage — a recolored belt painting what looks like PANTS,
                // because its LegUpperTexture slot decoded to nonsense across the
                // whole leg region. It also explains why the recolors looked perfect
                // in the viewer (which renders the PNG) and broken in the client.
                var blp = _blpWriter.EncodeBitmapToBlpUncompressed(bmp);
                if (blp == null)
                {
                    _logger.LogWarning("CommitBodyAtlas: BLP encode failed slot {Slot}", slot);
                    continue;
                }
                encoded.Add((slot, blp));
            }

            if (encoded.Count == 0)
            {
                result.Error = "No slots encoded to BLP";
                return result;
            }

            // One displayId for the whole piece; all slot rows share it.
            uint newDisplayId = await AllocateDisplayIdAsync();

            using (var conn = _db.Admin())
            {
                foreach (var (slot, blp) in encoded)
                {
                    string subdir = AtlasSlotSubdir[slot];
                    string blpName = $"Custom_{newDisplayId}_s{slot}_{SanitizeName(itemName)}.blp";
                    string blpMpqPath = $@"Item\TextureComponents\{subdir}\{blpName}";

                    await conn.ExecuteAsync(@"
                        INSERT INTO custom_item_retexture_atlas
                            (display_id, new_display_id, item_name, slot,
                             custom_blp_mpq_path, custom_blp, style_direction)
                        VALUES
                            (@DisplayId, @NewDisplayId, @ItemName, @Slot,
                             @BlpMpqPath, @BlpBytes, @Style)",
                        new
                        {
                            DisplayId = sourceDisplayId,
                            NewDisplayId = newDisplayId,
                            ItemName = itemName,
                            Slot = slot,
                            BlpMpqPath = blpMpqPath,
                            BlpBytes = blp,
                            Style = styleDirection
                        });
                }
            }

            _logger.LogInformation(
                "CommitBodyAtlas: Saved {Slots} slot(s) to DB — displayId {Old}→{New}",
                encoded.Count, sourceDisplayId, newDisplayId);

            // Register so SuperUI knows about the displayId immediately (clones
            // model + body textures from source for in-app GLB generation).
            _dbc.RegisterCustomDisplayEntry(newDisplayId, sourceDisplayId, null, null);

            // Deferred in batch mode — see the rebuildPatch param doc. The DB
            // rows above are the durable artifact; the patch is derived from them.
            PatchMRebuildResult rebuildResult = rebuildPatch
                ? await RebuildPatchMAsync()
                : new PatchMRebuildResult { Success = true };

            result.PatchMpqPath = rebuildResult.PatchWebPath;
            result.NewDisplayId = newDisplayId;
            result.BlpSizeBytes = encoded.Sum(e => e.Blp.Length);
            result.Success = rebuildResult.Success;
            if (!rebuildResult.Success)
                result.Error = rebuildResult.Error;

            _itemTextures.InvalidateCache(sourceDisplayId);
            _itemTextures.InvalidateCache(newDisplayId);

            _logger.LogInformation(
                "CommitBodyAtlas: Complete! displayId {Old}→{New}, {Slots} slots",
                sourceDisplayId, newDisplayId, encoded.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommitBodyAtlas: Failed for displayId {Id}", sourceDisplayId);
            result.Error = ex.Message;
            return result;
        }
    }

    // Filesystem/MPQ-safe short name for the BLP filename.
    private static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "armor";
        var chars = raw.Where(c => char.IsLetterOrDigit(c)).ToArray();
        var s = new string(chars);
        if (s.Length == 0) return "armor";
        return s.Length > 24 ? s.Substring(0, 24) : s;
    }

    // ═══════════════════════════════════════════════════════════════════
    // REBUILD PATCH-M.MPQ — ALL RETEXTURES (legacy section marker)
    // ═══════════════════════════════════════════════════════════════════

    public async Task<PatchMRebuildResult> RebuildPatchMAsync()
    {
        var result = new PatchMRebuildResult();

        try
        {
            await EnsureTableAsync();

            List<dynamic> retextures;
            using (var conn = _db.Admin())
            {
                retextures = (await conn.QueryAsync(
                    @"SELECT id, display_id, new_display_id, item_name,
                             texture_filename, custom_blp_mpq_path, custom_m2_mpq_path,
                             custom_blp, custom_m2
                      FROM custom_item_retexture
                      ORDER BY id")).ToList();
            }

            if (retextures.Count == 0)
            {
                _logger.LogInformation("Retexture: No retextures in DB, skipping patch-4 rebuild");
                result.Success = true;
                return result;
            }

            _logger.LogInformation("Retexture: Rebuilding patch-4.MPQ with {Count} retexture(s)",
                retextures.Count);

            // Load clean ItemDisplayInfo.dbc
            string dbcFile = Path.Combine(_dbcPath, "ItemDisplayInfo.dbc");
            if (!File.Exists(dbcFile))
            {
                result.Error = "ItemDisplayInfo.dbc not found";
                return result;
            }

            var displayDbc = DbcWriterService.ReadDbc(dbcFile);
            // A REAL logger, not null. It was `new MpqBuilderService(null)`, which
            // silenced every diagnostic inside the builder — including the hash-table
            // size line and the "REFUSING to build" error. The archive could wrap its
            // hash table to zero and emit a completely unreadable patch without a
            // single log line saying so. That is precisely how the ushort overflow
            // went unnoticed.
            var mpqBuilder = new MpqBuilderService(_loggerFactory.CreateLogger<MpqBuilderService>());

            foreach (var row in retextures)
            {
                uint origDisplayId = (uint)row.display_id;
                uint newDisplayId = (uint)row.new_display_id;
                string blpMpqPath = (string)row.custom_blp_mpq_path;
                byte[]? blpBytes = row.custom_blp as byte[];

                if (blpBytes != null && !string.IsNullOrEmpty(blpMpqPath))
                    mpqBuilder.AddFile(blpMpqPath, blpBytes);

                // Clone ItemDisplayInfo.dbc entry — DBC-only approach:
                // ModelName1 stays vanilla, only TextureName1 changes.
                var sourceRow = displayDbc.GetRow(origDisplayId);
                if (sourceRow != null && displayDbc.GetRow(newDisplayId) == null)
                {
                    displayDbc.CloneRow(origDisplayId, newDisplayId);

                    // Field 3 (TextureName1): custom BLP filename, no ext, no dir.
                    // Linux Path.GetFileName doesn't split on backslash — normalize first.
                    if (!string.IsNullOrEmpty(blpMpqPath))
                    {
                        string normalizedBlp = blpMpqPath.Replace('\\', '/');
                        string customTexName = Path.GetFileNameWithoutExtension(
                            Path.GetFileName(normalizedBlp));
                        uint texOfs = displayDbc.AddString(customTexName);
                        displayDbc.PatchRow(newDisplayId, 3, texOfs);
                    }
                }

                result.TotalEntries++;
            }

            // ── Body-atlas (painted armor) entries ──
            // One row PER SLOT, grouped by new_display_id. For each group: add
            // every slot BLP to the MPQ, clone the source ItemDisplayInfo row
            // once, then patch each slot's m_texture[] field (14 + slot) with
            // that slot's bare BLP name. Mirrors the weapon loop's clone/AddString
            // /PatchRow, just multi-field. Separate table, so weapons are untouched.
            List<dynamic> atlasRows;
            using (var conn = _db.Admin())
            {
                atlasRows = (await conn.QueryAsync(
                    @"SELECT display_id, new_display_id, slot,
                             custom_blp_mpq_path, custom_blp
                      FROM custom_item_retexture_atlas
                      ORDER BY new_display_id, slot")).ToList();
            }

            foreach (var grp in atlasRows.GroupBy(r => (uint)r.new_display_id))
            {
                uint newDisplayId = grp.Key;
                uint origDisplayId = (uint)grp.First().display_id;

                // Clone the source row ONCE per displayId (guard against an
                // already-present row, same as the weapon loop).
                bool cloned = displayDbc.GetRow(newDisplayId) != null;
                if (!cloned && displayDbc.GetRow(origDisplayId) != null)
                {
                    displayDbc.CloneRow(origDisplayId, newDisplayId);
                    cloned = true;
                }

                foreach (var row in grp)
                {
                    int slot = (int)(byte)row.slot;
                    string blpMpqPath = (string)row.custom_blp_mpq_path;
                    byte[]? blpBytes = row.custom_blp as byte[];

                    // Component BLPs are GENDER-SUFFIXED in the client. See
                    // AddComponentBlpAllGenders — packing only "{name}.blp" makes
                    // the cloned DBC row blank the component instead of recoloring
                    // it, because the client only ever asks for "{name}_M|F|U.blp".
                    if (blpBytes != null && !string.IsNullOrEmpty(blpMpqPath))
                        AddComponentBlpAllGenders(mpqBuilder, blpMpqPath, blpBytes);

                    // m_texture[slot] is DBC field (14 + slot). Bare name, no ext,
                    // no dir, NO gender suffix — the client prepends
                    // Item\TextureComponents\{subdir}\ and appends _{M|F|U}.blp.
                    if (cloned && slot >= 0 && slot <= 7 && !string.IsNullOrEmpty(blpMpqPath))
                    {
                        string normalizedBlp = blpMpqPath.Replace('\\', '/');
                        string customTexName = Path.GetFileNameWithoutExtension(
                            Path.GetFileName(normalizedBlp));
                        uint texOfs = displayDbc.AddString(customTexName);
                        displayDbc.PatchRow(newDisplayId, 14 + slot, texOfs);
                    }
                }

                result.TotalEntries++;
            }

            byte[] patchedDbc = displayDbc.Write();
            mpqBuilder.AddFile(@"DBFilesClient\ItemDisplayInfo.dbc", patchedDbc);

            var patchDir = Path.Combine(_env.WebRootPath, "patches", "retexture");
            Directory.CreateDirectory(patchDir);
            string patchPath = Path.Combine(patchDir, "patch-4.MPQ");

            // Drop the patched DBC next to the archive as a plain file. It is the
            // single artifact the CLIENT reads and that nothing else in the pipeline
            // can inspect — once it is sealed inside the MPQ it is a black box. Every
            // silent failure today (zeroed hash table, DXT3 components, bare BLP
            // names) shared that shape: the thing we could not look at was the thing
            // that was wrong. So write it out and make it inspectable.
            try
            {
                File.WriteAllBytes(Path.Combine(patchDir, "ItemDisplayInfo.dbc"), patchedDbc);
                _logger.LogInformation(
                    "Retexture: wrote patched ItemDisplayInfo.dbc ({Bytes} bytes, {Rows} rows) for inspection",
                    patchedDbc.Length, displayDbc.RecordCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retexture: could not dump patched DBC for inspection");
            }

            bool built = mpqBuilder.Build(patchPath);
            if (!built)
            {
                result.Error = "Failed to build patch-4.MPQ";
                return result;
            }

            // Deploy to WoW client Data folder
            string? clientDataPath = _config["Vmangos:ClientDataPath"]
                ?? _config["SpellCreator:ClientDataPath"];
            if (!string.IsNullOrEmpty(clientDataPath) && Directory.Exists(clientDataPath))
            {
                string deployedPath = Path.Combine(clientDataPath, "patch-4.MPQ");
                File.Copy(patchPath, deployedPath, overwrite: true);
                _logger.LogInformation("Retexture: Deployed patch-4.MPQ to {Path} ({Count} retextures)",
                    deployedPath, result.TotalEntries);
            }

            result.PatchWebPath = "/patches/retexture/patch-4.MPQ";
            result.MpqFileCount = mpqBuilder.FileCount;
            result.Success = true;

            _logger.LogInformation(
                "Retexture: patch-4.MPQ rebuilt — {Count} retextures, {Files} files, {Size}KB",
                result.TotalEntries, mpqBuilder.FileCount,
                new FileInfo(patchPath).Length / 1024);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retexture: patch-4 rebuild failed");
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Absolute path to the served patch inside wwwroot — the single location
    /// DownloadPatch reads from and RebuildPatchMAsync writes to.
    /// </summary>
    public string PatchWwwrootPath =>
        Path.Combine(_env.WebRootPath, "patches", "retexture", "patch-4.MPQ");

    /// <summary>
    /// True if at least one retexture exists in either table, i.e. a patch can
    /// be produced. Drives whether the download button shows — based on the DB
    /// (durable), not the wwwroot file (ephemeral: wiped on publish/restart).
    /// </summary>
    public async Task<bool> HasAnyRetexturesAsync()
    {
        await EnsureTableAsync();
        using var conn = _db.Admin();
        var count = await conn.ExecuteScalarAsync<long>(
            @"SELECT (SELECT COUNT(*) FROM custom_item_retexture)
                   + (SELECT COUNT(*) FROM custom_item_retexture_atlas)");
        return count > 0;
    }

    /// <summary>
    /// Guarantees the served patch exists in wwwroot. wwwroot is ephemeral — a
    /// publish/restart wipes wwwroot/patches — but the retextures are durable in
    /// the DB, so if the file is missing we regenerate it from the DB on demand.
    /// Returns the patch path, or null if there are no retextures to build from.
    /// </summary>
    public async Task<string?> EnsurePatchBuiltAsync()
    {
        var path = PatchWwwrootPath;
        if (File.Exists(path)) return path;

        if (!await HasAnyRetexturesAsync()) return null;

        _logger.LogInformation(
            "Retexture: served patch missing from wwwroot (likely wiped on redeploy) — regenerating from DB");
        var rebuild = await RebuildPatchMAsync();
        return rebuild.Success && File.Exists(path) ? path : null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // LIST / DELETE / STARTUP
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Call at startup to register all existing retextures in DbcService's
    /// in-memory caches so custom displayIds work immediately.
    /// </summary>
    public async Task LoadExistingRetexturesAsync()
    {
        await EnsureTableAsync();
        using var conn = _db.Admin();
        var rows = await conn.QueryAsync(
            @"SELECT display_id, new_display_id, custom_m2_mpq_path, custom_blp_mpq_path
              FROM custom_item_retexture");

        int count = 0;
        foreach (var row in rows)
        {
            uint origId = (uint)row.display_id;
            uint newId = (uint)row.new_display_id;

            // Register with original model/texture names (null = clone from source)
            _dbc.RegisterCustomDisplayEntry(newId, origId, null, null);
            count++;
        }

        // Body-atlas displayIds (one row per slot — register each distinct
        // new_display_id once) so painted-armor retextures resolve after restart.
        var atlasRows = await conn.QueryAsync(
            @"SELECT DISTINCT display_id, new_display_id FROM custom_item_retexture_atlas");
        int atlasCount = 0;
        foreach (var row in atlasRows)
        {
            _dbc.RegisterCustomDisplayEntry((uint)row.new_display_id, (uint)row.display_id, null, null);
            atlasCount++;
        }
        if (atlasCount > 0)
            _logger.LogInformation("Retexture: Loaded {Count} body-atlas retextures into DBC cache", atlasCount);

        if (count > 0)
            _logger.LogInformation("Retexture: Loaded {Count} existing retextures into DBC cache", count);
    }

    public async Task<List<RetextureEntry>> ListRetexturesAsync()
    {
        await EnsureTableAsync();
        using var conn = _db.Admin();
        return (await conn.QueryAsync<RetextureEntry>(
            @"SELECT id AS Id, display_id AS DisplayId, new_display_id AS NewDisplayId,
                     item_name AS ItemName, texture_filename AS TextureFilename,
                     custom_blp_mpq_path AS CustomBlpMpqPath,
                     custom_m2_mpq_path AS CustomM2MpqPath,
                     style_direction AS StyleDirection,
                     created_at AS CreatedAt
              FROM custom_item_retexture ORDER BY id DESC")).ToList();
    }

    public async Task<bool> DeleteRetextureAsync(int id)
    {
        using var conn = _db.Admin();
        int affected = await conn.ExecuteAsync(
            "DELETE FROM custom_item_retexture WHERE id = @Id", new { Id = id });

        if (affected > 0)
        {
            await RebuildPatchMAsync();
            return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // DISPLAY ID ALLOCATION
    // ═══════════════════════════════════════════════════════════════════

    private async Task<uint> AllocateDisplayIdAsync()
    {
        using var conn = _db.Admin();
        // MAX across BOTH retexture tables — body-atlas displayIds live in
        // custom_item_retexture_atlas, so ignoring it would hand out an ID that
        // collides with an existing painted-armor entry.
        var maxId = await conn.ExecuteScalarAsync<uint?>(
            @"SELECT MAX(m) FROM (
                  SELECT MAX(new_display_id) AS m FROM custom_item_retexture
                  UNION ALL
                  SELECT MAX(new_display_id) AS m FROM custom_item_retexture_atlas
              ) t");

        uint next = Math.Max((maxId ?? 0) + 1, CUSTOM_DISPLAY_BASE);

        string dbcFile = Path.Combine(_dbcPath, "ItemDisplayInfo.dbc");
        if (File.Exists(dbcFile))
        {
            var dbc = DbcWriterService.ReadDbc(dbcFile);
            uint dbcMax = dbc.GetMaxId();
            if (next <= dbcMax)
                next = dbcMax + 1;
        }

        return next;
    }

    // ═══════════════════════════════════════════════════════════════════
    // OLLAMA — Texture Prompt Crafting
    // ═══════════════════════════════════════════════════════════════════

    private async Task<string> CraftTexturePromptAsync(
        string itemName, string styleDirection, string textureFilename,
        int width, int height, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_ollamaUrl) || string.IsNullOrEmpty(_ollamaModel))
            return BuildDefaultTexturePrompt(itemName, styleDirection, textureFilename);

        string systemPrompt = $"""
            You translate a user's creative direction into an image prompt for FLUX (a text-to-image AI).

            CRITICAL CONTEXT: The output image will be used as a TEXTURE MAP — a flat 2D image that gets
            UV-wrapped onto a 3D model in World of Warcraft (vanilla, 2004 era). It is NOT a picture of an object.

            RULES FOR THE PROMPT YOU GENERATE:
            - MUST describe a FLAT, TOP-DOWN surface/material — like a photo of a tabletop, fabric swatch, or metal sheet
            - MUST include: "flat 2D texture, top-down view, no perspective, no 3D rendering, no shadows"
            - MUST describe colors, materials, and surface patterns
            - MUST include "hand-painted style, World of Warcraft vanilla aesthetic"
            - NEVER describe a 3D object, weapon, character, scene, or environment
            - NEVER include words like "hammer", "sword", "shield", "blade", "hilt" as physical objects
            - Instead translate those into MATERIAL descriptions: "hilt texture" → "wrapped leather with brass studs"
            - The texture is {width}x{height} pixels

            Respond with ONLY the prompt text. No explanation. One paragraph.
            /no_think
            """;

        string userPrompt = $"Item: \"{itemName}\". Texture file: \"{textureFilename}\".";
        if (!string.IsNullOrEmpty(styleDirection))
            userPrompt += $" Creative direction: \"{styleDirection}\"";
        else
            userPrompt += " Generate a texture that fits this item's theme.";

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _ollamaModel,
                prompt = userPrompt,
                system = systemPrompt,
                stream = false,
                keep_alive = "5m",
                options = new { temperature = 0.7, num_predict = 150, num_ctx = 4096 }
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(OllamaTimeout);

            var resp = await _http.PostAsync(_ollamaUrl,
                new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            string? raw = doc.RootElement.GetProperty("response").GetString()?.Trim();

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var thinkStart = raw.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                if (thinkStart >= 0)
                {
                    var thinkEnd = raw.IndexOf("</think>", thinkStart, StringComparison.OrdinalIgnoreCase);
                    if (thinkEnd >= 0) raw = raw[(thinkEnd + 8)..].Trim();
                }
                return raw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retexture: Ollama prompt crafting failed, using default");
        }

        return BuildDefaultTexturePrompt(itemName, styleDirection, textureFilename);
    }

    private static string BuildDefaultTexturePrompt(string itemName, string style, string filename)
    {
        string material = "metal";
        string fLower = filename.ToLowerInvariant();
        if (fLower.Contains("wood") || fLower.Contains("staff") || fLower.Contains("bow"))
            material = "weathered wood grain with carved runes";
        else if (fLower.Contains("leather") || fLower.Contains("quiver"))
            material = "tooled leather with stitching";
        else if (fLower.Contains("cloth") || fLower.Contains("robe"))
            material = "woven fabric with embroidered pattern";
        else if (fLower.Contains("hammer") || fLower.Contains("mace"))
            material = "forged dark iron with metallic sheen";
        else if (fLower.Contains("sword") || fLower.Contains("blade"))
            material = "polished steel with etched patterns";

        string styleStr = string.IsNullOrEmpty(style) ? $"fantasy medieval {material}" : $"{style}, {material}";

        return $"Flat 2D texture map, top-down view, {styleStr}, " +
               "hand-painted style, World of Warcraft vanilla 2004 aesthetic, " +
               "no perspective, no 3D rendering, no shadows, no objects, " +
               "seamless tileable surface material, fill entire frame with material detail";
    }

    // ═══════════════════════════════════════════════════════════════════
    // COMFYUI WORKFLOWS
    // ═══════════════════════════════════════════════════════════════════

    private Dictionary<string, object> BuildTextureWorkflow(string prompt, uint displayId, int width, int height)
    {
        return new Dictionary<string, object>
        {
            ["1"] = new Dictionary<string, object> { ["class_type"] = "UnetLoaderGGUF", ["inputs"] = new Dictionary<string, object> { ["unet_name"] = "flux1-dev-Q5_K_S.gguf" } },
            ["2"] = new Dictionary<string, object> { ["class_type"] = "DualCLIPLoader", ["inputs"] = new Dictionary<string, object> { ["clip_name1"] = "clip_l.safetensors", ["clip_name2"] = _clipModel2, ["type"] = "flux" } },
            ["3"] = new Dictionary<string, object> { ["class_type"] = "CLIPTextEncode", ["inputs"] = new Dictionary<string, object> { ["text"] = prompt, ["clip"] = new object[] { "2", 0 } } },
            ["5"] = new Dictionary<string, object> { ["class_type"] = "EmptySD3LatentImage", ["inputs"] = new Dictionary<string, object> { ["width"] = width, ["height"] = height, ["batch_size"] = 1 } },
            ["6"] = new Dictionary<string, object> { ["class_type"] = "KSampler", ["inputs"] = new Dictionary<string, object> { ["model"] = new object[] { "1", 0 }, ["positive"] = new object[] { "3", 0 }, ["negative"] = new object[] { "3", 0 }, ["latent_image"] = new object[] { "5", 0 }, ["seed"] = Random.Shared.Next(), ["steps"] = 25, ["cfg"] = 1.0, ["sampler_name"] = "euler", ["scheduler"] = "simple", ["denoise"] = 1.0 } },
            ["7"] = new Dictionary<string, object> { ["class_type"] = "VAEDecode", ["inputs"] = new Dictionary<string, object> { ["samples"] = new object[] { "6", 0 }, ["vae"] = new object[] { "8", 0 } } },
            ["8"] = new Dictionary<string, object> { ["class_type"] = "VAELoader", ["inputs"] = new Dictionary<string, object> { ["vae_name"] = "ae.safetensors" } },
            ["9"] = new Dictionary<string, object> { ["class_type"] = "SaveImage", ["inputs"] = new Dictionary<string, object> { ["images"] = new object[] { "7", 0 }, ["filename_prefix"] = $"retexture_{displayId}" } }
        };
    }

    private Dictionary<string, object> BuildImg2ImgWorkflow(string prompt, string uploadedImageName, uint displayId, int width, int height, float denoise)
    {
        return new Dictionary<string, object>
        {
            ["1"] = new Dictionary<string, object> { ["class_type"] = "UnetLoaderGGUF", ["inputs"] = new Dictionary<string, object> { ["unet_name"] = "flux1-dev-Q5_K_S.gguf" } },
            ["2"] = new Dictionary<string, object> { ["class_type"] = "DualCLIPLoader", ["inputs"] = new Dictionary<string, object> { ["clip_name1"] = "clip_l.safetensors", ["clip_name2"] = _clipModel2, ["type"] = "flux" } },
            ["3"] = new Dictionary<string, object> { ["class_type"] = "CLIPTextEncode", ["inputs"] = new Dictionary<string, object> { ["text"] = prompt, ["clip"] = new object[] { "2", 0 } } },
            ["10"] = new Dictionary<string, object> { ["class_type"] = "LoadImage", ["inputs"] = new Dictionary<string, object> { ["image"] = uploadedImageName } },
            ["11"] = new Dictionary<string, object> { ["class_type"] = "ImageScale", ["inputs"] = new Dictionary<string, object> { ["image"] = new object[] { "10", 0 }, ["width"] = width, ["height"] = height, ["upscale_method"] = "lanczos", ["crop"] = "center" } },
            ["12"] = new Dictionary<string, object> { ["class_type"] = "VAEEncode", ["inputs"] = new Dictionary<string, object> { ["pixels"] = new object[] { "11", 0 }, ["vae"] = new object[] { "8", 0 } } },
            ["6"] = new Dictionary<string, object> { ["class_type"] = "KSampler", ["inputs"] = new Dictionary<string, object> { ["model"] = new object[] { "1", 0 }, ["positive"] = new object[] { "3", 0 }, ["negative"] = new object[] { "3", 0 }, ["latent_image"] = new object[] { "12", 0 }, ["seed"] = Random.Shared.Next(), ["steps"] = 25, ["cfg"] = 1.0, ["sampler_name"] = "euler", ["scheduler"] = "simple", ["denoise"] = denoise } },
            ["7"] = new Dictionary<string, object> { ["class_type"] = "VAEDecode", ["inputs"] = new Dictionary<string, object> { ["samples"] = new object[] { "6", 0 }, ["vae"] = new object[] { "8", 0 } } },
            ["8"] = new Dictionary<string, object> { ["class_type"] = "VAELoader", ["inputs"] = new Dictionary<string, object> { ["vae_name"] = "ae.safetensors" } },
            ["9"] = new Dictionary<string, object> { ["class_type"] = "SaveImage", ["inputs"] = new Dictionary<string, object> { ["images"] = new object[] { "7", 0 }, ["filename_prefix"] = $"retexture_i2i_{displayId}" } }
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // M2 HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static readonly string[] ItemModelPrefixes = new[]
    {
        @"Item\ObjectComponents\Weapon\", @"Item\ObjectComponents\Shield\",
        @"Item\ObjectComponents\Head\", @"Item\ObjectComponents\Shoulder\",
        @"Item\ObjectComponents\Quiver\", @"Item\ObjectComponents\Ammo\",
        @"Creature\", @"World\",
    };

    private byte[]? FindAndExtractItemM2(string modelName)
    {
        if (modelName.Contains('\\') || modelName.Contains('/'))
            return _mpq.ExtractModelFile(modelName);

        var baseName = Path.GetFileNameWithoutExtension(modelName);
        foreach (var prefix in ItemModelPrefixes)
        {
            var data = _mpq.ExtractModelFile(prefix + baseName + ".m2");
            if (data != null) return data;
            data = _mpq.ExtractModelFile(prefix + baseName + ".mdx");
            if (data != null) return data;
            data = _mpq.ExtractModelFile(prefix + baseName.ToLowerInvariant() + ".m2");
            if (data != null) return data;
        }
        return null;
    }

    private static string GuessM2Directory(string modelName)
    {
        if (modelName.Contains('\\') || modelName.Contains('/'))
        {
            string dir = Path.GetDirectoryName(modelName)?.Replace('/', '\\') ?? "";
            return string.IsNullOrEmpty(dir) ? "" : dir + "\\";
        }

        string lower = modelName.ToLowerInvariant();
        if (lower.Contains("sword") || lower.Contains("axe") || lower.Contains("mace") ||
            lower.Contains("dagger") || lower.Contains("hammer") || lower.Contains("staff") ||
            lower.Contains("bow") || lower.Contains("gun") || lower.Contains("polearm") ||
            lower.Contains("weapon"))
            return @"Item\ObjectComponents\Weapon\";
        if (lower.Contains("shield") || lower.Contains("buckler"))
            return @"Item\ObjectComponents\Shield\";
        if (lower.Contains("helm") || lower.Contains("head") || lower.Contains("crown"))
            return @"Item\ObjectComponents\Head\";
        if (lower.Contains("shoulder") || lower.Contains("pauldron"))
            return @"Item\ObjectComponents\Shoulder\";

        return @"Item\ObjectComponents\Weapon\";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════════════

public class RetextureRequest
{
    public uint DisplayId { get; set; }
    public string ItemName { get; set; } = "";
    public string OriginalBlpFilename { get; set; } = "";
    public string OriginalMpqPath { get; set; } = "";
    public string StyleDirection { get; set; } = "";
    public string? CustomPrompt { get; set; }
    public bool ModifyExisting { get; set; }
    public float DenoiseStrength { get; set; } = 0.5f;
}

public class RetextureResult
{
    public uint DisplayId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Prompt { get; set; }
    public string? GeneratedPngPath { get; set; }
    public string? CustomBlpMpqPath { get; set; }
    public string? CustomM2MpqPath { get; set; }
    public string? PatchMpqPath { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public string? OriginalFormat { get; set; }
    public int BlpSizeBytes { get; set; }
    public uint NewDisplayId { get; set; }
}

public class PatchMRebuildResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? PatchWebPath { get; set; }
    public int TotalEntries { get; set; }

    /// <summary>
    /// Files actually packed into the archive. NOT the same as TotalEntries, which
    /// counts DB rows/display-groups. This is the number that decides the MPQ hash
    /// table size — and therefore whether the archive is readable at all.
    /// </summary>
    public int MpqFileCount { get; set; }
}

public class RetextureEntry
{
    public int Id { get; set; }
    public uint DisplayId { get; set; }
    public uint NewDisplayId { get; set; }
    public string ItemName { get; set; } = "";
    public string TextureFilename { get; set; } = "";
    public string CustomBlpMpqPath { get; set; } = "";
    public string CustomM2MpqPath { get; set; } = "";
    public string StyleDirection { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}