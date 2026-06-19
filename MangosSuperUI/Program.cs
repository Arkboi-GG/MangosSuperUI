using MangosSuperUI.Services;
using MangosSuperUI.Models;
using MangosSuperUI.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Diagnostics.Metrics;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Planners;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd();  // ← sends watchdog heartbeats + handles SIGTERM gracefully

// ---------- Brain log buffer (in-memory ring, fed to the Bots "Live" tab on demand) ----------
var botLogBuffer = new BotLogBuffer();
builder.Services.AddSingleton(botLogBuffer);
builder.Logging.AddProvider(new BotLogBufferProvider(botLogBuffer));
builder.Logging.AddFilter<BotLogBufferProvider>("MangosSuperUI", LogLevel.Debug);

// ---------- Additional Config Source ----------
builder.Configuration.AddJsonFile("server-config.json", optional: true, reloadOnChange: true);

// ---------- Configuration ----------
builder.Services.Configure<VmangosSettings>(builder.Configuration.GetSection("Vmangos"));
builder.Services.Configure<RemoteAccessSettings>(builder.Configuration.GetSection("RemoteAccess"));

// ---------- Data ----------
builder.Services.AddSingleton<ConnectionFactory>();

// ---------- Services ----------
builder.Services.AddSingleton<DbInitializationService>();
builder.Services.AddSingleton<RaService>();
builder.Services.AddSingleton<ProcessManagerService>();
builder.Services.AddSingleton<StateCaptureService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<DbcService>();
builder.Services.AddSingleton<HeightMapService>();
builder.Services.AddSingleton<BotBridgeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BotBridgeService>());
builder.Services.AddSingleton<OllamaChatService>();
builder.Services.AddSingleton<SourceIndexerService>();
builder.Services.AddSingleton<ZoneSafetyMap>();
builder.Services.AddSingleton<BotFleetDiagnostics>();
builder.Services.AddSingleton<BotFlightRecorder>();
builder.Services.AddSingleton<SpellCreatorService>();
builder.Services.AddSingleton<BlpWriterService>();
builder.Services.AddSingleton<PatchBuilderService>();
builder.Services.AddSingleton<SpellIconService>();
builder.Services.AddSingleton<SpellConfigService>();
builder.Services.AddSingleton<SpellTextureService>();
builder.Services.AddSingleton<SpellRecipeService>();
builder.Services.AddSingleton<ComfyUIDispatcher>();
builder.Services.AddSingleton<ComfyUIUpscaler>();
builder.Services.AddSingleton<VanillaBlpService>();
builder.Services.AddSingleton<SpellDnaService>();
builder.Services.AddSingleton<MpqReaderService>();
builder.Services.AddSingleton<CharacterModelService>();
builder.Services.AddSingleton<BodyAtlasTextureService>();
builder.Services.AddSingleton<CacheVersionRegistry>();
builder.Services.AddSingleton<CharacterSkinCompositor>();
builder.Services.AddSingleton<PaletteSwapService>();
builder.Services.AddScoped<VariationRecipeService>();
builder.Services.AddScoped<TextureSegmentationService>();
builder.Services.AddSingleton<VramManager>();

builder.Services.AddScoped<ItemTextureService>();
builder.Services.AddScoped<ItemRetextureService>();

// ---------- BotLogic: Behavioral Engine ----------

// Tracking (in-memory, singleton)
builder.Services.AddSingleton<BotStateTracker>();
builder.Services.AddSingleton<BotActivityLog>();
builder.Services.AddSingleton<BotRelationships>();

// Data loaders
builder.Services.AddSingleton<QuirkLoader>();
builder.Services.AddSingleton<SpellProgressionLoader>();
builder.Services.AddSingleton<ZoneDataLoader>();
builder.Services.AddSingleton<QuestGraphLoader>();
builder.Services.AddSingleton<BotBrainDbInit>();
builder.Services.AddSingleton<SpellProgressionLoader>();

// Core engine
builder.Services.AddSingleton<LiveStateModifiers>();

// Brain spine (Strategy B rebuild): executor + supervisor + driver
builder.Services.AddSingleton<BotExecutor>();
builder.Services.AddSingleton<BotSupervisor>();

// Brain planners (Phase 2 — Grinding): goal selector + per-goal planners (IBotPlanner).
// BotBrain self-assembles the Goal→planner map from the registered IBotPlanner set;
// adding a goal in P3+ is one more AddSingleton<IBotPlanner, …>() here.
builder.Services.AddSingleton<GoalSelector>();
builder.Services.AddSingleton<IBotPlanner, GrindPlanner>();
builder.Services.AddSingleton<IBotPlanner, QuestPlanner>();   // P3: Goal.Questing
builder.Services.AddSingleton<IBotPlanner, MaintenancePlanner>();

builder.Services.AddSingleton<BotBrain>();

// Brain orchestrator (BackgroundService)
builder.Services.AddSingleton<BotBrainService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BotBrainService>());


// ---------- MVC + SignalR ----------
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var retexService = scope.ServiceProvider.GetRequiredService<ItemRetextureService>();
    retexService.LoadExistingRetexturesAsync().GetAwaiter().GetResult();

    var registry = scope.ServiceProvider.GetRequiredService<CacheVersionRegistry>();
    registry.SweepAllOnStartup();
}

// ---------- Database Bootstrap ----------
// Ensures vmangos_admin DB + tables exist before any request can hit AuditService.
// Never throws — logs errors and sets AdminDbReady = false for dashboard to display.
var dbInit = app.Services.GetRequiredService<DbInitializationService>();
await dbInit.InitializeAsync();


await app.Services.GetRequiredService<QuestGraphLoader>().LoadAsync();
await app.Services.GetRequiredService<ZoneSafetyMap>().LoadAsync();
// Vendor/innkeeper NPC cache — backs MaintenancePlanner.GetNearestVendor. Was registered
// as a singleton but its LoadAsync was never invoked at boot, so _vendorsByMap stayed empty
// and every vendor lookup returned null ("no vendors loaded on this map"). Load it here, same
// as the other startup loaders.
await app.Services.GetRequiredService<ZoneDataLoader>().LoadAsync();

// ---------- Pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Static files with custom MIME types (GLB for 3D model-viewer)
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ConsoleHub>("/hubs/console");
app.MapHub<LogStreamHub>("/hubs/logs");
app.MapHub<BotBridgeHub>("/hubs/botbridge");

app.Run();