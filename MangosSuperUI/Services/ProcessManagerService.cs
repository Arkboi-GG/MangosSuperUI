using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace MangosSuperUI.Services;

public class ProcessManagerService
{
    private readonly IOptionsMonitor<VmangosSettings> _settingsMonitor;
    private readonly ILogger<ProcessManagerService> _logger;
    private readonly RaService _raService;

    // Cache the auto-detected process names so we don't re-scan /proc every poll
    private string? _resolvedMangosdName;
    private string? _resolvedRealmdName;
    private DateTime _lastResolveScan = DateTime.MinValue;
    private static readonly TimeSpan ResolveCacheDuration = TimeSpan.FromSeconds(30);

    // Windows graceful-shutdown tuning (see StopWindowsProcessAsync / TryGracefulShutdownAsync).
    private const int GracefulShutdownDelaySec = 5;   // "server shutdown N" countdown sent over RA
    private const int GracefulExitGraceSec = 30;      // extra time to let mangosd save + exit before force-kill

    public ProcessManagerService(
        IOptionsMonitor<VmangosSettings> settings,
        ILogger<ProcessManagerService> logger,
        RaService raService)
    {
        _settingsMonitor = settings;
        _logger = logger;
        _raService = raService;
    }

    private VmangosSettings Settings => _settingsMonitor.CurrentValue;

    public ProcessStatus GetMangosdStatus() => GetProcessStatus("mangosd", Settings.MangosdProcess);
    public ProcessStatus GetRealmdStatus() => GetProcessStatus("realmd", Settings.RealmdProcess);

    public Task<string> StartMangosdAsync() => ControlAsync("start", "mangosd", Settings.MangosdProcess);
    public Task<string> StopMangosdAsync() => ControlAsync("stop", "mangosd", Settings.MangosdProcess);
    public Task<string> RestartMangosdAsync() => ControlAsync("restart", "mangosd", Settings.MangosdProcess);

    public Task<string> StartRealmdAsync() => ControlAsync("start", "realmd", Settings.RealmdProcess);
    public Task<string> StopRealmdAsync() => ControlAsync("stop", "realmd", Settings.RealmdProcess);
    public Task<string> RestartRealmdAsync() => ControlAsync("restart", "realmd", Settings.RealmdProcess);

    /// <summary>
    /// Dispatches process control to the platform-appropriate mechanism:
    /// systemctl on Linux, direct executable start/kill on Windows.
    /// </summary>
    private Task<string> ControlAsync(string action, string keyword, string configuredName)
    {
        if (OperatingSystem.IsWindows())
            return RunWindowsProcessControlAsync(action, keyword, configuredName);
        return RunSystemctlAsync(action, keyword);
    }

    /// <summary>
    /// Returns diagnostics about process detection — what name was configured,
    /// what was actually found, and how it was resolved.
    /// </summary>
    public ProcessDiagnostics GetDiagnostics()
    {
        var diag = new ProcessDiagnostics
        {
            ConfiguredMangosd = Settings.MangosdProcess,
            ConfiguredRealmd = Settings.RealmdProcess
        };

        // Force a fresh scan for diagnostics
        _lastResolveScan = DateTime.MinValue;

        var mangosdStatus = GetProcessStatus("mangosd", Settings.MangosdProcess);
        var realmdStatus = GetProcessStatus("realmd", Settings.RealmdProcess);

        diag.ResolvedMangosd = _resolvedMangosdName;
        diag.ResolvedRealmd = _resolvedRealmdName;
        diag.MangosdRunning = mangosdStatus.IsRunning;
        diag.RealmdRunning = realmdStatus.IsRunning;
        diag.MangosdPid = mangosdStatus.Pid;
        diag.RealmdPid = realmdStatus.Pid;

        // Check if configured name matches resolved name
        if (mangosdStatus.IsRunning && _resolvedMangosdName != null
            && !string.Equals(Settings.MangosdProcess, _resolvedMangosdName, StringComparison.Ordinal))
        {
            diag.MangosdNameMismatch = true;
            diag.MangosdHint = $"Configured as '{Settings.MangosdProcess}' but /proc reports '{_resolvedMangosdName}'. "
                + "Update the process name in Settings or it may show offline on some systems.";
        }

        if (realmdStatus.IsRunning && _resolvedRealmdName != null
            && !string.Equals(Settings.RealmdProcess, _resolvedRealmdName, StringComparison.Ordinal))
        {
            diag.RealmdNameMismatch = true;
            diag.RealmdHint = $"Configured as '{Settings.RealmdProcess}' but /proc reports '{_resolvedRealmdName}'. "
                + "Update the process name in Settings or it may show offline on some systems.";
        }

        return diag;
    }

    private async Task<string> RunSystemctlAsync(string action, string unit)
    {
        _logger.LogInformation("Running systemctl {Action} {Unit}", action, unit);

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"systemctl {action} {unit}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException($"Failed to start systemctl {action} {unit}");

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            _logger.LogError("systemctl {Action} {Unit} failed (exit {Code}): {Error}", action, unit, proc.ExitCode, stderr);
            throw new InvalidOperationException($"systemctl {action} {unit} failed: {stderr.Trim()}");
        }

        // Invalidate cached process names after start/restart so next poll re-scans
        if (action is "start" or "restart")
        {
            _lastResolveScan = DateTime.MinValue;
        }

        _logger.LogInformation("systemctl {Action} {Unit} succeeded", action, unit);
        return stdout.Trim();
    }

    /// <summary>
    /// Windows control path. mangosd/realmd are treated as plain executables under
    /// Vmangos:BinDirectory (as produced by a CMake/Visual Studio build) — no service
    /// manager is assumed. Start launches the exe detached in its own console window
    /// with Vmangos:RunDirectory as the working directory; stop shuts mangosd down
    /// cleanly over RA (falling back to a hard kill); restart is stop-then-start.
    /// </summary>
    private async Task<string> RunWindowsProcessControlAsync(string action, string keyword, string configuredName)
    {
        _logger.LogInformation("Windows process control: {Action} {Name}", action, configuredName);

        switch (action)
        {
            case "start":
                return StartWindowsProcess(keyword, configuredName);
            case "stop":
                return await StopWindowsProcessAsync(keyword, configuredName);
            case "restart":
                await StopWindowsProcessAsync(keyword, configuredName);
                await Task.Delay(1500); // let the OS release listening ports before relaunch
                return StartWindowsProcess(keyword, configuredName);
            default:
                throw new ArgumentException($"Unknown action '{action}'");
        }
    }

    private string StartWindowsProcess(string keyword, string configuredName)
    {
        // Mirror `systemctl start` on an already-active unit: treat as a no-op success.
        var existing = GetProcessStatus(keyword, configuredName);
        if (existing.IsRunning)
            return $"{configuredName} is already running (PID {existing.Pid})";

        if (string.IsNullOrWhiteSpace(Settings.BinDirectory))
            throw new InvalidOperationException(
                $"Vmangos:BinDirectory is not set. Point it at the folder containing {StripExe(configuredName)}.exe.");

        var exeName = StripExe(configuredName) + ".exe";
        var exePath = Path.Combine(Settings.BinDirectory, exeName);
        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"Could not find '{exeName}' in '{Settings.BinDirectory}'. "
                + "Check Vmangos:BinDirectory and the configured process name.", exePath);

        // The executable lives in BinDirectory, but mangosd/realmd resolve their .conf
        // and data/ relative to the working directory. Prefer an explicit RunDirectory;
        // fall back to BinDirectory when it isn't configured.
        var workingDir = !string.IsNullOrWhiteSpace(Settings.RunDirectory)
            ? Settings.RunDirectory
            : Settings.BinDirectory;
        if (!Directory.Exists(workingDir))
            throw new DirectoryNotFoundException(
                $"Working directory '{workingDir}' does not exist. Check Vmangos:RunDirectory (or BinDirectory).");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            UseShellExecute = true // own console window, detached from the web app's lifetime
        };

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {exePath}");

        _logger.LogInformation("Started {Exe} (PID {Pid}, cwd {Cwd})", exePath, proc.Id, workingDir);
        return $"{configuredName} started (PID {proc.Id})";
    }

    /// <summary>
    /// Stops a Windows process. mangosd carries live world state, so it is asked to shut
    /// down cleanly over RA first ("server shutdown N", which saves players/AH/etc.);
    /// realmd is stateless and goes straight to terminate. A hard kill is the fallback
    /// whenever the graceful path is unavailable or the process doesn't exit in time.
    /// </summary>
    private async Task<string> StopWindowsProcessAsync(string keyword, string configuredName)
    {
        var winName = StripExe(configuredName);

        if (Process.GetProcessesByName(winName).Length == 0)
            return $"{configuredName} is not running";

        if (keyword == "mangosd")
        {
            var graceful = await TryGracefulShutdownAsync(winName);
            if (graceful != null)
                return graceful;
            _logger.LogWarning("Graceful RA shutdown unavailable for {Name}; terminating process.", winName);
        }

        return KillWindowsProcess(winName, configuredName);
    }

    /// <summary>
    /// Asks mangosd to shut down cleanly via the RA console ("server shutdown N") and
    /// waits for the process to exit. Returns a success message if it exits within the
    /// grace window, or null (caller falls back to a hard kill) when RA is unreachable
    /// or the process is still alive once the window elapses.
    /// </summary>
    private async Task<string?> TryGracefulShutdownAsync(string winName)
    {
        bool sent;
        try
        {
            _logger.LogInformation("Requesting graceful shutdown of {Name} via RA (server shutdown {Delay})",
                winName, GracefulShutdownDelaySec);
            await _raService.SendCommandAsync($"server shutdown {GracefulShutdownDelaySec}");
            sent = true;
        }
        catch (Exception ex)
        {
            // Reading the reply can fail as the server stops responding mid-shutdown, so
            // the command may still have landed. Poll briefly for exit before giving up
            // rather than assuming failure — but keep the wait short since RA may be down.
            _logger.LogWarning(ex, "RA 'server shutdown' reply not received for {Name}", winName);
            sent = false;
        }

        var windowSec = sent ? GracefulShutdownDelaySec + GracefulExitGraceSec : GracefulShutdownDelaySec + 5;
        var deadline = TimeSpan.FromSeconds(windowSec);
        var step = TimeSpan.FromMilliseconds(500);
        for (var waited = TimeSpan.Zero; waited < deadline; waited += step)
        {
            if (Process.GetProcessesByName(winName).Length == 0)
            {
                _logger.LogInformation("{Name} shut down gracefully after ~{Sec:0.0}s", winName, waited.TotalSeconds);
                return $"{winName} shut down gracefully";
            }
            await Task.Delay(step);
        }

        return null;
    }

    private string KillWindowsProcess(string winName, string configuredName)
    {
        var procs = Process.GetProcessesByName(winName);
        if (procs.Length == 0)
            return $"{configuredName} is not running";

        var killed = 0;
        foreach (var p in procs)
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
                killed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate {Name} PID {Pid}", winName, p.Id);
            }
        }

        if (killed == 0)
            throw new InvalidOperationException(
                $"Found {procs.Length} '{winName}' process(es) but could not terminate any (permission denied?).");

        return $"{configuredName} stopped ({killed} process(es) terminated)";
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// Gets process status using a multi-strategy approach:
    /// 1. Try the configured process name via Process.GetProcessesByName
    /// 2. If not found, scan /proc for any process whose comm or cmdline contains the keyword
    /// 3. Cache the resolved name so subsequent polls are fast
    /// </summary>
    private ProcessStatus GetProcessStatus(string keyword, string configuredName)
    {
        // Windows has no /proc, so the comm-scan and bin-directory ownership checks
        // used by the Linux strategies below can never match. Detect by process name.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var winName = StripExe(configuredName);
                var proc = Process.GetProcessesByName(winName).FirstOrDefault();
                if (proc != null)
                {
                    UpdateResolvedName(keyword, winName);
                    return new ProcessStatus
                    {
                        IsRunning = true,
                        Pid = proc.Id,
                        ProcessName = winName,
                        StartTime = TryGetStartTime(proc),
                        Uptime = TryGetUptime(proc)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Windows process lookup for {Name} failed", configuredName);
            }
            return new ProcessStatus { IsRunning = false };
        }

        // Strategy 1: Try configured name directly (fast path)
        try
        {
            var processes = Process.GetProcessesByName(configuredName);
            var proc = processes.FirstOrDefault(p => IsProcessFromConfiguredBinDirectory(p.Id));
            if (proc != null)
            {
                UpdateResolvedName(keyword, configuredName);
                return new ProcessStatus
                {
                    IsRunning = true,
                    Pid = proc.Id,
                    ProcessName = configuredName,
                    StartTime = TryGetStartTime(proc),
                    Uptime = TryGetUptime(proc)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetProcessesByName({Name}) failed, falling back to /proc scan", configuredName);
        }

        // Strategy 2: Try the cached resolved name (if different from configured)
        var cached = keyword == "mangosd" ? _resolvedMangosdName : _resolvedRealmdName;
        if (cached != null && cached != configuredName)
        {
            try
            {
                var processes = Process.GetProcessesByName(cached);
                var proc = processes.FirstOrDefault(p => IsProcessFromConfiguredBinDirectory(p.Id));
                if (proc != null)
                {
                    return new ProcessStatus
                    {
                        IsRunning = true,
                        Pid = proc.Id,
                        ProcessName = cached,
                        StartTime = TryGetStartTime(proc),
                        Uptime = TryGetUptime(proc)
                    };
                }
            }
            catch { }
        }

        // Strategy 3: Scan /proc (expensive — throttled to once per ResolveCacheDuration)
        if (DateTime.UtcNow - _lastResolveScan > ResolveCacheDuration)
        {
            var found = ScanProcForProcess(keyword);
            if (found != null)
            {
                UpdateResolvedName(keyword, found.Value.commName);
                _lastResolveScan = DateTime.UtcNow;

                return new ProcessStatus
                {
                    IsRunning = true,
                    Pid = found.Value.pid,
                    ProcessName = found.Value.commName,
                    StartTime = TryGetStartTimeByPid(found.Value.pid),
                    Uptime = TryGetUptimeByPid(found.Value.pid)
                };
            }
            _lastResolveScan = DateTime.UtcNow;
        }

        return new ProcessStatus { IsRunning = false };
    }

    /// <summary>
    /// Scans /proc/*/comm and /proc/*/cmdline for a process matching the keyword.
    /// This catches cases where the binary is "mangosd" but /proc/comm reports "mangosd-main",
    /// or the binary was renamed, or it runs under screen/tmux.
    /// </summary>
    private (int pid, string commName)? ScanProcForProcess(string keyword)
    {
        try
        {
            var procDirs = Directory.GetDirectories("/proc")
                .Where(d => int.TryParse(Path.GetFileName(d), out _))
                .ToArray();

            foreach (var dir in procDirs)
            {
                var pid = int.Parse(Path.GetFileName(dir));

                // Check /proc/PID/comm first (the "short" name, max 15 chars)
                var commPath = Path.Combine(dir, "comm");
                if (File.Exists(commPath))
                {
                    try
                    {
                        var comm = File.ReadAllText(commPath).Trim();
                        if (comm.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            && IsProcessFromConfiguredBinDirectory(pid))
                        {
                            _logger.LogInformation(
                                "Process auto-detect: found {Keyword} via /proc/{Pid}/comm = '{Comm}'",
                                keyword, pid, comm);
                            return (pid, comm);
                        }
                    }
                    catch { }
                }

                // Check /proc/PID/cmdline (full command line, null-separated)
                var cmdlinePath = Path.Combine(dir, "cmdline");
                if (File.Exists(cmdlinePath))
                {
                    try
                    {
                        var cmdline = File.ReadAllText(cmdlinePath).Replace('\0', ' ').Trim();
                        // Only match on the executable name, not arguments
                        var exe = cmdline.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                        var exeName = Path.GetFileName(exe);
                        if (exeName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            && IsProcessFromConfiguredBinDirectory(pid))
                        {
                            // Read the actual comm name for this PID
                            var actualComm = File.Exists(commPath)
                                ? File.ReadAllText(commPath).Trim()
                                : exeName;

                            _logger.LogInformation(
                                "Process auto-detect: found {Keyword} via /proc/{Pid}/cmdline, comm='{Comm}'",
                                keyword, pid, actualComm);
                            return (pid, actualComm);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan /proc for {Keyword}", keyword);
        }

        return null;
    }

    /// <summary>
    /// Restricts process discovery to this SuperUI instance's configured server directory.
    /// Multiple emulator installations can legitimately use the same mangosd/realmd process
    /// names on one host, so matching on /proc/comm alone is not enough to establish ownership.
    /// </summary>
    private bool IsProcessFromConfiguredBinDirectory(int pid)
    {
        if (string.IsNullOrWhiteSpace(Settings.BinDirectory))
            return true;

        try
        {
            var cmdlinePath = $"/proc/{pid}/cmdline";
            if (!File.Exists(cmdlinePath))
                return false;

            var cmdline = File.ReadAllText(cmdlinePath);
            var executable = cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable))
                return false;

            var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executable));
            var configuredDirectory = Path.GetFullPath(Settings.BinDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(executableDirectory, configuredDirectory, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to verify configured bin directory for PID {Pid}", pid);
            return false;
        }
    }

    private void UpdateResolvedName(string keyword, string name)
    {
        if (keyword == "mangosd")
            _resolvedMangosdName = name;
        else
            _resolvedRealmdName = name;
    }

    private static DateTime? TryGetStartTime(Process proc)
    {
        try { return proc.StartTime; } catch { return null; }
    }

    private static TimeSpan? TryGetUptime(Process proc)
    {
        try { return DateTime.Now - proc.StartTime; } catch { return null; }
    }

    private static DateTime? TryGetStartTimeByPid(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return proc.StartTime;
        }
        catch { return null; }
    }

    private static TimeSpan? TryGetUptimeByPid(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return DateTime.Now - proc.StartTime;
        }
        catch { return null; }
    }
}

public class ProcessStatus
{
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public string? ProcessName { get; set; }
    public DateTime? StartTime { get; set; }
    public TimeSpan? Uptime { get; set; }
}

public class ProcessDiagnostics
{
    public string? ConfiguredMangosd { get; set; }
    public string? ConfiguredRealmd { get; set; }
    public string? ResolvedMangosd { get; set; }
    public string? ResolvedRealmd { get; set; }
    public bool MangosdRunning { get; set; }
    public bool RealmdRunning { get; set; }
    public int? MangosdPid { get; set; }
    public int? RealmdPid { get; set; }
    public bool MangosdNameMismatch { get; set; }
    public bool RealmdNameMismatch { get; set; }
    public string? MangosdHint { get; set; }
    public string? RealmdHint { get; set; }
}
