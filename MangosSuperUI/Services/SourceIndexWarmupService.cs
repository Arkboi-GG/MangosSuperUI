namespace MangosSuperUI.Services;

/// <summary>
/// Builds the Source Map index in the background at startup so /SourceMap is never
/// stuck on the April legacy graph after a restart. The index is in-memory only and
/// was lost on every deploy; nobody pressed Reindex, so Body/FileContent answered
/// "No index" and the SuiBots files (the ones Circuit traces point at) were invisible.
/// A full index of ~1,000 files takes ~3 s on the box, so this is cheap to do eagerly.
/// Skips silently when the configured source path does not exist (Windows dev box).
/// </summary>
public sealed class SourceIndexWarmupService : IHostedService
{
    private readonly SourceIndexerService _indexer;
    private readonly IConfiguration _config;
    private readonly ILogger<SourceIndexWarmupService> _log;

    public SourceIndexWarmupService(SourceIndexerService indexer, IConfiguration config, ILogger<SourceIndexWarmupService> log)
    {
        _indexer = indexer;
        _config = config;
        _log = log;
    }

    /// <summary>Same resolution as SourceMapController.Reindex so the two never disagree.</summary>
    public static string ResolveSourcePath(IConfiguration config)
        => config["Vmangos:VmangosSourcePath"] ?? "/home/wowvmangos/vmangos/src";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        string path = ResolveSourcePath(_config);
        if (!Directory.Exists(path))
        {
            _log.LogInformation("[SOURCEMAP] source path {Path} not present — index not built (Reindex later if it appears)", path);
            return Task.CompletedTask;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await _indexer.ReindexAsync(path);
                if (r.Success)
                    _log.LogInformation("[SOURCEMAP] startup index built: {Files} files, {Symbols} symbols, {Types} types in {Ms} ms",
                        r.Files, r.Symbols, r.Types, r.ElapsedMs);
                else
                    _log.LogWarning("[SOURCEMAP] startup index failed: {Error}", r.Error);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[SOURCEMAP] startup index threw");
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
