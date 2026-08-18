using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Automatic sketch → 3D on the app's ComfyUI pool — same shape as the other ComfyUI features
/// (spell icons, texture img2img): the app owns the job, the user never opens ComfyUI.
///
/// The image→3D workflow itself is NOT hardcoded: the owner installs an image→3D node pack on the
/// ComfyUI server once (the Weapon Forge page's setup dialog carries complete agent instructions),
/// exports the working workflow with "Save (API format)", and uploads that JSON in the same dialog.
/// It is stored durably in vmangos_admin.weapon_forge_config (wwwroot/app dir are wiped on publish).
/// From then on a sketch upload: evicts whatever model is resident on the GPU
/// (<see cref="ComfyUIDispatcher.FreeVramAsync"/> → POST /free), uploads the image, substitutes its
/// stored filename into the workflow's LoadImage node, dispatches, and downloads the produced .glb.
/// </summary>
public sealed class ComfyUIWeapon3DGenerator : IWeapon3DGenerator
{
    private const string WorkflowKey = "image3d_workflow_json";

    private readonly ComfyUIDispatcher _dispatcher;
    private readonly ConnectionFactory _db;
    private readonly ILogger<ComfyUIWeapon3DGenerator> _logger;

    // Cache of the stored workflow; refreshed on save/clear and loaded on first use.
    private volatile string? _workflowJson;
    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public ComfyUIWeapon3DGenerator(ComfyUIDispatcher dispatcher, ConnectionFactory db,
        ILogger<ComfyUIWeapon3DGenerator> logger)
    {
        _dispatcher = dispatcher;
        _db = db;
        _logger = logger;
    }

    /// <summary>True once a workflow JSON has been uploaded (last-known cached state; refreshed by
    /// every setup-dialog fetch, save, clear, and generation attempt).</summary>
    public bool IsConfigured => _workflowJson is not null;

    // ═══════════════════════════════════════════════════════════════════
    // WORKFLOW STORAGE
    // ═══════════════════════════════════════════════════════════════════

    public async Task<Image3DWorkflowInfo> GetWorkflowInfoAsync()
    {
        await EnsureLoadedAsync();
        var json = _workflowJson;
        if (json is null)
            return new Image3DWorkflowInfo { Present = false, NodeCount = 0, HasLoadImage = false };

        var (nodeCount, hasLoadImage) = Describe(json);
        return new Image3DWorkflowInfo { Present = true, NodeCount = nodeCount, HasLoadImage = hasLoadImage };
    }

    /// <summary>Validate and store the API-format workflow JSON. Errors are plain-English and the
    /// stored workflow is only replaced when the new one passes.</summary>
    public async Task<(bool Ok, string Message)> SaveWorkflowAsync(string workflowJson)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(workflowJson); }
        catch (Exception ex) { return (false, $"Not valid JSON: {ex.Message}"); }

        if (root is not JsonObject obj || obj.Count == 0)
            return (false, "Expected an API-format workflow: a JSON object of node-id → node.");

        // API format: { "3": { "class_type": "...", "inputs": { ... } }, ... }. Some exports wrap it
        // as { "prompt": { ... } } — unwrap that transparently.
        if (obj.Count <= 2 && obj["prompt"] is JsonObject inner) obj = inner;

        int nodes = 0; bool hasLoadImage = false;
        foreach (var (_, node) in obj)
        {
            if (node is not JsonObject nodeObj || nodeObj["class_type"] is null)
                return (false, "This looks like the UI-format save. Use “Save (API format)” in ComfyUI — every node must have a class_type.");
            nodes++;
            if (string.Equals((string?)nodeObj["class_type"], "LoadImage", StringComparison.OrdinalIgnoreCase))
                hasLoadImage = true;
        }
        if (!hasLoadImage)
            return (false, "No LoadImage node found — the workflow needs one so the sketch can be fed in.");

        string canonical = obj.ToJsonString();
        await using (var conn = _db.Admin())
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                @"INSERT INTO weapon_forge_config (k, v) VALUES (@k, @v)
                  ON DUPLICATE KEY UPDATE v = VALUES(v)",
                new { k = WorkflowKey, v = canonical });
        }
        _workflowJson = canonical;
        _loaded = true;
        _logger.LogInformation("WeaponForge: image→3D workflow saved ({Nodes} nodes)", nodes);
        return (true, $"Workflow saved — {nodes} nodes, LoadImage found. Sketch → 3D is active.");
    }

    public async Task ClearWorkflowAsync()
    {
        await using (var conn = _db.Admin())
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM weapon_forge_config WHERE k = @k", new { k = WorkflowKey });
        }
        _workflowJson = null;
        _loaded = true;
        _logger.LogInformation("WeaponForge: image→3D workflow removed");
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await _loadLock.WaitAsync();
        try
        {
            if (_loaded) return;
            await using var conn = _db.Admin();
            await conn.OpenAsync();
            _workflowJson = await conn.QueryFirstOrDefaultAsync<string?>(
                "SELECT v FROM weapon_forge_config WHERE k = @k", new { k = WorkflowKey });
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WeaponForge: could not load image→3D workflow (treating as not configured)");
            _loaded = true;
        }
        finally { _loadLock.Release(); }
    }

    private static (int NodeCount, bool HasLoadImage) Describe(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return (0, false);
            int nodes = 0; bool hasLoad = false;
            foreach (var (_, node) in obj)
            {
                nodes++;
                if (node is JsonObject o && string.Equals((string?)o["class_type"], "LoadImage", StringComparison.OrdinalIgnoreCase))
                    hasLoad = true;
            }
            return (nodes, hasLoad);
        }
        catch { return (0, false); }
    }

    // ═══════════════════════════════════════════════════════════════════
    // GENERATION
    // ═══════════════════════════════════════════════════════════════════

    public async Task<Weapon3DGenerationResult> GenerateGlbAsync(byte[] imageBytes, CancellationToken ct = default)
        => await GenerateGlbAsync(new Dictionary<string, byte[]> { ["front"] = imageBytes }, ct);

    /// <summary>Multiview-capable workflow dispatch. Name LoadImage nodes WF_FRONT, WF_LEFT,
    /// WF_BACK, WF_RIGHT in ComfyUI (node title or role input) to bind inspected workbench views.
    /// Untitled/legacy workflows remain compatible and receive the front image.</summary>
    public async Task<Weapon3DGenerationResult> GenerateGlbAsync(
        IReadOnlyDictionary<string, byte[]> views, CancellationToken ct = default)
    {
        await EnsureLoadedAsync();
        var json = _workflowJson;
        if (json is null)
            return new Weapon3DGenerationResult(false, null,
                "Automatic sketch → 3D is not set up yet. Open “Set up sketch → 3D” on this page: it has the install " +
                "instructions AND the upload field for the finished workflow JSON. Until then, a .glb made in any 3D " +
                "tool forges identically via “Import GLB”.");

        // 1) Upload every inspected reference. Legacy graphs use front; named graphs bind roles.
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, bytes) in views)
        {
            string uploadName = $"forge_{role}_{Guid.NewGuid():N}.png";
            var (storedName, _) = await _dispatcher.UploadImageAsync(bytes, uploadName, ct);
            if (storedName is null)
                return new Weapon3DGenerationResult(false, null,
                    $"Could not upload the {role} reference to ComfyUI — check the pool on this page.");
            stored[role] = storedName;
        }
        if (!stored.TryGetValue("front", out var frontName))
            return new Weapon3DGenerationResult(false, null, "A front reference is required.");

        // 2) Substitute the stored filename into the workflow's LoadImage node(s).
        JsonObject workflow;
        try { workflow = (JsonObject)JsonNode.Parse(json)!; }
        catch (Exception ex)
        {
            return new Weapon3DGenerationResult(false, null, $"Stored workflow no longer parses ({ex.Message}) — re-upload it in “Set up sketch → 3D”.");
        }
        int substituted = 0, roleBound = 0;
        foreach (var (_, node) in workflow)
        {
            if (node is JsonObject o &&
                string.Equals((string?)o["class_type"], "LoadImage", StringComparison.OrdinalIgnoreCase) &&
                o["inputs"] is JsonObject inputs)
            {
                string title = ((string?)o["_meta"]?["title"] ?? (string?)inputs["weapon_forge_role"] ?? "").Trim();
                string? role = new[] { "front", "left", "back", "right" }
                    .FirstOrDefault(r => title.Contains("WF_" + r.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(title, r, StringComparison.OrdinalIgnoreCase));
                if (role is not null && stored.TryGetValue(role, out var named))
                {
                    inputs["image"] = named; roleBound++;
                }
                else inputs["image"] = frontName;
                substituted++;
            }
        }
        if (substituted == 0)
            return new Weapon3DGenerationResult(false, null,
                "The stored workflow has no LoadImage node to feed the sketch into — re-export it from ComfyUI and re-upload.");

        // 3) Evict the resident model (FLUX, …) and run. The dispatcher discovers and downloads the
        //    first .glb the job produces; failures carry ComfyUI's actual reason (a validation
        //    rejection never reaches /history, so this is the only place it surfaces).
        _logger.LogInformation("WeaponForge: dispatching sketch→3D ({Views} view(s), {Bound} named slot(s))", stored.Count, roleBound);
        var (glb, dispatchError) = await _dispatcher.GenerateFileBytesDetailedAsync(workflow, "sketch→3D", new[] { ".glb" }, ct, freeVramFirst: true);
        if (glb is null or { Length: 0 })
            return new Weapon3DGenerationResult(false, null,
                $"Image→3D failed: {dispatchError ?? "no .glb produced"}. If it names a node input outside its min/max, " +
                "the workflow's values conflict with the node's declared limits — fix either in ComfyUI and re-upload.");

        _logger.LogInformation("WeaponForge: sketch→3D produced {Bytes:N0} byte GLB", glb.Length);
        return new Weapon3DGenerationResult(true, glb, $"Reconstructed {glb.Length:N0} byte GLB.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETUP PROMPT (unchanged contract — the dialog's copy-paste agent instructions)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// The copy-paste prompt the owner hands to an AI agent (Claude, etc.) with access to the target
    /// machine. Self-contained, and it curates itself to the owner's situation: with a configured
    /// ComfyUI pool it is an add-a-node-pack prompt; with NO pool configured it becomes a genuine
    /// from-scratch prompt — hardware check (with an honest stop if the GPU can't do it), ComfyUI
    /// install, network exposure, then the pack — written for owners with zero local-AI experience.
    /// Both variants end the same way: hand back the API-format workflow JSON, which is uploaded in
    /// the same setup dialog.
    /// </summary>
    public static string BuildAgentSetupPrompt(IReadOnlyList<(string Name, string BaseUrl)> nodes)
    {
        var sb = new StringBuilder();
        if (nodes.Count == 0)
        {
            sb.AppendLine("I have NO local AI setup — no ComfyUI, nothing installed, and I don't know this ecosystem.");
            sb.AppendLine("You are setting up image→3D generation from scratch on this machine so my MangosSuperUI app");
            sb.AppendLine("can turn hand-drawn weapon sketches into 3D models (.glb). Assume nothing is installed and");
            sb.AppendLine("explain what you're doing as you go.");
            sb.AppendLine();
            sb.AppendLine("AUTHORIZATION");
            sb.AppendLine("You have my explicit authorization to inspect this machine and to install ComfyUI, packages,");
            sb.AppendLine("and model weights, and to configure/restart the resulting service. If a workspace policy file");
            sb.AppendLine("(AGENTS.md, CLAUDE.md, …) forbids you from acting, say EXACTLY THAT in your FIRST reply and");
            sb.AppendLine("stop — do not burn a session on a read-only partial run. And even though I believe nothing is");
            sb.AppendLine("installed, do a quick whole-disk check for existing ComfyUI/TRELLIS/Hunyuan installs and model");
            sb.AppendLine("caches before downloading anything — reuse whatever exists.");
            sb.AppendLine();
            sb.AppendLine("STEP 0 — HARDWARE CHECK (do this FIRST, stop if it fails)");
            sb.AppendLine("Identify the GPU and VRAM (nvidia-smi on a machine with NVIDIA drivers; otherwise check the");
            sb.AppendLine("OS device info). Image→3D models realistically need a modern NVIDIA GPU with 12 GB+ VRAM");
            sb.AppendLine("(some Hunyuan3D variants squeeze into 8 GB, slowly). If this machine has no suitable GPU,");
            sb.AppendLine("STOP HERE and report that plainly — do not install anything. In that case my options are a");
            sb.AppendLine("different machine on my network or a rented cloud GPU, and drawing → 3D can wait; the app's");
            sb.AppendLine("“Import GLB” path works without any of this.");
            sb.AppendLine();
            sb.AppendLine("STEP 1 — INSTALL COMFYUI");
            sb.AppendLine("Install ComfyUI itself (on Windows the portable release is easiest; on Linux, git clone plus");
            sb.AppendLine("its own venv per the README). Verify it starts and the web UI loads before going further.");
            sb.AppendLine();
            sb.AppendLine("STEP 2 — MAKE IT REACHABLE");
            sb.AppendLine("My MangosSuperUI app calls ComfyUI over HTTP. If the app runs on a different machine, start");
            sb.AppendLine("ComfyUI with --listen 0.0.0.0 (default port 8188) and open the firewall for it on the local");
            sb.AppendLine("network only. Note the final URL (http://<this-machine>:8188) — I need it for the app's");
            sb.AppendLine("Weapon Forge → Pipeline setup page.");
            sb.AppendLine();
            sb.AppendLine("GOAL");
        }
        else
        {
            sb.AppendLine("You are setting up automatic image→3D generation on my ComfyUI server so my MangosSuperUI app");
            sb.AppendLine("can turn hand-drawn weapon sketches into 3D models (.glb). The app dispatches ComfyUI jobs");
            sb.AppendLine("over the HTTP API (POST /prompt, GET /history/{id}, GET /view) — your job is to get a working");
            sb.AppendLine("image→3D pipeline REACHABLE THROUGH THAT API and hand back its API-format workflow JSON.");
            sb.AppendLine();
            sb.AppendLine("AUTHORIZATION");
            sb.AppendLine("You have my explicit authorization to inspect this whole machine and to install packages,");
            sb.AppendLine("download weights, modify ComfyUI's custom_nodes, and restart the ComfyUI service as needed for");
            sb.AppendLine("this task. If a workspace policy file (AGENTS.md, CLAUDE.md, …) forbids you from acting, say");
            sb.AppendLine("EXACTLY THAT in your FIRST reply and stop — do not burn a session on a read-only partial run");
            sb.AppendLine("and report failure at the end.");
            sb.AppendLine();
            sb.AppendLine("MY COMFYUI SERVER(S)");
            foreach (var (name, url) in nodes)
                sb.AppendLine($"- {name}: {url}");
            sb.AppendLine();
            sb.AppendLine("STEP 0 — INVENTORY THE ENTIRE MACHINE (mandatory, before deciding anything)");
            sb.AppendLine("Existing image→3D installations are LIKELY on this box and reuse beats reinstalling. The live");
            sb.AppendLine("ComfyUI's node registry proves nothing about the rest of the machine — check the FULL host:");
            sb.AppendLine("- every ComfyUI checkout (find / -path '*/ComfyUI/main.py' across /home /opt /srv), and each");
            sb.AppendLine("  one's custom_nodes;");
            sb.AppendLine("- STANDALONE 3D repos outside ComfyUI (TRELLIS / Hunyuan3D / ai-tools / similar directories)");
            sb.AppendLine("  with their own venvs — pip list every venv for trellis/hunyuan/kaolin/spconv/nvdiffrast;");
            sb.AppendLine("- model weight caches (~/.cache/huggingface, models dirs) — never re-download weights that");
            sb.AppendLine("  already exist, point the integration at them;");
            sb.AppendLine("- containers, systemd services, and every listening port (a working pipeline may be a service");
            sb.AppendLine("  that isn't ComfyUI at all).");
            sb.AppendLine("Report what you found BEFORE installing anything. If I tell you something exists here, it");
            sb.AppendLine("almost certainly does — keep looking until you find it or have searched the whole disk.");
            sb.AppendLine();
            sb.AppendLine("INTEGRATION RULE");
            sb.AppendLine("My app speaks ONLY the ComfyUI HTTP API. If the best working pipeline found in Step 0 is a");
            sb.AppendLine("standalone install (not a ComfyUI pack), BRIDGE it into ComfyUI rather than duplicating it:");
            sb.AppendLine("install the matching ComfyUI node pack pointed at the existing weights/env, or write a thin");
            sb.AppendLine("custom node that invokes the standalone pipeline and saves its .glb into ComfyUI's output");
            sb.AppendLine("directory. State which bridge you chose and why.");
            sb.AppendLine();
            sb.AppendLine("GOAL");
        }
        sb.AppendLine("One ComfyUI workflow that: takes a front weapon sketch via LoadImage (optionally inspected");
        sb.AppendLine("left/back/right views too) → reconstructs a textured 3D mesh → SAVES it as a single .glb");
        sb.AppendLine("into ComfyUI's output directory, so it shows up in GET /history/{prompt_id} outputs with a");
        sb.AppendLine("filename ending in .glb (that is how my app discovers and downloads the result). Single static");
        sb.AppendLine("mesh, embedded or PNG texture, no animation/rig.");
        sb.AppendLine("For a multiview-capable graph, add LoadImage nodes whose UI titles are exactly WF_FRONT,");
        sb.AppendLine("WF_LEFT, WF_BACK, and WF_RIGHT. The app binds only the views inspected/enabled in its Forge");
        sb.AppendLine("workbench. A legacy graph with one unnamed LoadImage remains valid and receives WF_FRONT.");
        sb.AppendLine();
        sb.AppendLine("REQUIRED STAGING (do not reconstruct directly at the final game budget)");
        sb.AppendLine("- Build a high-resolution internal/master shape first, with enough structure resolution and sampling");
        sb.AppendLine("  to establish blade thickness, bevel, guard depth, grip and pommel volume.");
        sb.AppendLine("- Refine and texture that master, then produce the final low-poly mesh last. Preserve blade/guard");
        sb.AppendLine("  silhouette and UV seams and bake useful relief/AO into base color; blind collapse-decimation fails.");
        sb.AppendLine("- Only the final saved GLB must be ≈600–900 triangles. My app HARD-REJECTS above 1,000 triangles,");
        sb.AppendLine("  and the file format caps vertices at 65,535. Intermediate geometry stays inside the workflow.");
        sb.AppendLine("- If you write or wrap a custom node, its parameter constraints (min/max/step) MUST permit these");
        sb.AppendLine("  low targets — e.g. final remesh min ≤ 500, texture min ≤ 512. A min of 10,000 makes every valid");
        sb.AppendLine("  game-budget value fail prompt validation.");
        sb.AppendLine("- Texture 512×512 is plenty (my app downsamples to a 256² game texture regardless).");
        sb.AppendLine("- Prefer settings that keep thin blades hole-free at low polygon counts over raw fidelity.");
        sb.AppendLine("- ACCEPTANCE ADDITION: verify the final .glb really is ≤ 1,000 triangles before reporting done.");
        sb.AppendLine();
        sb.AppendLine("VRAM CONSTRAINT (important)");
        sb.AppendLine("This GPU may also serve the app's other AI features (FLUX image generation for icons/textures),");
        sb.AppendLine("now or later. Before each 3D job my app calls");
        sb.AppendLine("POST {server}/free with body {\"unload_models\":true,\"free_memory\":true} to kick resident models");
        sb.AppendLine("off the card, and ComfyUI reloads them on demand afterwards. So:");
        sb.AppendLine("1. Verify POST /free works on my ComfyUI version (curl it, expect HTTP 200).");
        sb.AppendLine("2. Pick a pack whose models load per-job through normal ComfyUI nodes (loadable/evictable on");
        sb.AppendLine("   demand), NOT a sidecar daemon that pins VRAM at startup.");
        sb.AppendLine("3. Confirm the 3D model fits in VRAM on its own right after eviction.");
        sb.AppendLine();
        sb.AppendLine("WHAT TO INSTALL (only what the machine is actually missing — reuse anything already present)");
        sb.AppendLine("Research current options first — this space moves fast. Candidates as of my last check:");
        sb.AppendLine("- Hunyuan3D 2.x ComfyUI nodes — usually the easiest solid image→3D with textured GLB export.");
        sb.AppendLine("- TRELLIS node packs (e.g. IF_Trellis / ComfyUI-Trellis) — excellent quality, heavier Python");
        sb.AppendLine("  dependencies (spconv, kaolin, flash-attn, …).");
        sb.AppendLine("Pick ONE that (a) installs cleanly against this machine's CUDA/torch, (b) outputs GLB, and");
        sb.AppendLine("(c) meets the VRAM constraint. Install via ComfyUI-Manager, or git clone into custom_nodes and");
        sb.AppendLine("pip install its requirements into ComfyUI's own venv; download the model weights wherever the");
        sb.AppendLine("pack's README says. Restart ComfyUI after installing.");
        sb.AppendLine();
        sb.AppendLine("ACCEPTANCE (all must pass before you're done)");
        sb.AppendLine("1. In the ComfyUI web UI, run the workflow once on a test weapon image → a .glb is produced and");
        sb.AppendLine("   opens in a viewer.");
        sb.AppendLine("2. Export the working workflow with “Save (API format)”.");
        sb.AppendLine("3. Prove it headless over HTTP: POST /free (200) → POST /prompt with the API JSON (200 +");
        sb.AppendLine("   prompt_id) → poll GET /history/{prompt_id} until outputs list the .glb filename →");
        sb.AppendLine("   GET /view?filename=...&type=output downloads a valid GLB.");
        sb.AppendLine("4. Run a normal FLUX image job afterwards to confirm the card recovers for other workloads.");
        sb.AppendLine();
        sb.AppendLine("REPORT BACK");
        if (nodes.Count == 0)
        {
            sb.AppendLine("1. The ComfyUI URL (http://<machine>:8188) — I will enter it directly in Weapon Forge →");
            sb.AppendLine("   Pipeline setup so the node joins the live pool without an app restart.");
            sb.AppendLine("2. The full API-format workflow JSON (plus the pack name/weights you installed, for the");
            sb.AppendLine("   record). That JSON gets uploaded in the same setup dialog this prompt came from — the app");
            sb.AppendLine("   substitutes the sketch into the LoadImage node at runtime, no code changes needed.");
            sb.AppendLine("3. If Step 0 failed: say clearly that this machine can't run image→3D and why, and stop.");
        }
        else
        {
            sb.AppendLine("1. The full API-format workflow JSON. It gets uploaded in the same setup dialog this prompt");
            sb.AppendLine("   came from — the app substitutes the sketch into the LoadImage node at runtime, no code");
            sb.AppendLine("   changes needed.");
            sb.AppendLine("2. The Step 0 inventory summary: what already existed, what you reused, what you added, and");
            sb.AppendLine("   which bridge you chose if the pipeline was standalone.");
            sb.AppendLine("3. The pack/weights involved (name + version), for the record.");
        }
        return sb.ToString();
    }
}

/// <summary>Status of the stored image→3D workflow, for the setup dialog.</summary>
public sealed class Image3DWorkflowInfo
{
    public required bool Present { get; init; }
    public required int NodeCount { get; init; }
    public required bool HasLoadImage { get; init; }
}
