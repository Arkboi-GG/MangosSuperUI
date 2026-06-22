using System.Diagnostics;
using System.Reflection;

namespace MangosSuperUI.Services;

// ============================================================================
//  BotDiagnosticsService — runs the journald quantizers from inside the service
// ============================================================================
// The bounded-output diagnostic scripts (bot_run_report.sh, bot_diag.sh) used to
// live loose at ~/temp and had to be run by hand. This service makes them travel
// WITH the assembly: the two scripts are compiled in as EmbeddedResource, and at
// call time we extract the chosen one to a temp file and execute it under bash,
// capturing stdout. Nothing on the box has to "find" the scripts anymore.
//
// SETUP (one time): drop the real bot_run_report.sh and bot_diag.sh into the
// project (e.g. BotLogic/Diagnostics/) and set Build Action = Embedded Resource:
//
//   <ItemGroup>
//     <EmbeddedResource Include="BotLogic\Diagnostics\bot_run_report.sh" />
//     <EmbeddedResource Include="BotLogic\Diagnostics\bot_diag.sh" />
//   </ItemGroup>
//
// Resources are matched by filename suffix, so the namespace prefix is irrelevant.
// Register in Program.cs:  builder.Services.AddSingleton<BotDiagnosticsService>();
//
// NOTE: the scripts read journald. The process user must be able to read the
// journal (member of systemd-journal, or the unit run with the right perms) —
// any permission failure comes back verbatim in Stderr rather than being hidden.
public sealed class BotDiagnosticsService
{
    private readonly ILogger<BotDiagnosticsService> _log;
    private readonly string _workDir;

    public const string RunReportScript = "bot_run_report.sh";
    public const string BotDiagScript = "bot_diag.sh";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    public BotDiagnosticsService(ILogger<BotDiagnosticsService> log)
    {
        _log = log;
        _workDir = Path.Combine(Path.GetTempPath(), "mangossuperui-diag");
        Directory.CreateDirectory(_workDir);
    }

    public bool RunReportAvailable => FindResource(RunReportScript) != null;
    public bool BotDiagAvailable => FindResource(BotDiagScript) != null;

    /// <summary>Run bot_run_report.sh. Optional pid pins a past run; null auto-detects the live PID.</summary>
    public Task<DiagResult> RunFleetReportAsync(int? pid = null, CancellationToken ct = default)
    {
        var args = pid is > 0 ? new[] { pid.Value.ToString() } : Array.Empty<string>();
        return RunScriptAsync(RunReportScript, args, ct);
    }

    /// <summary>Run bot_diag.sh &lt;BotName&gt; (the per-bot drill-down; it auto-locates the last driven PID).</summary>
    public Task<DiagResult> RunBotDiagAsync(string botName, CancellationToken ct = default)
    {
        botName = (botName ?? "").Trim();
        // The name is passed as a process argument (not a shell string), but keep it
        // strictly alnum/_/- so it can never be coerced into anything surprising.
        if (botName.Length == 0 || botName.Length > 32 || !botName.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            return Task.FromResult(DiagResult.Fail("Invalid bot name (letters, digits, _ or - only)."));
        return RunScriptAsync(BotDiagScript, new[] { botName }, ct);
    }

    private async Task<DiagResult> RunScriptAsync(string scriptName, string[] args, CancellationToken ct)
    {
        var resName = FindResource(scriptName);
        if (resName == null)
            return DiagResult.Fail($"{scriptName} is not embedded in the service. Add it as an EmbeddedResource and rebuild — see BotDiagnosticsService header.");

        string path;
        try
        {
            path = await ExtractAsync(resName, scriptName, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to extract diagnostic script {Script}", scriptName);
            return DiagResult.Fail("Could not extract the script: " + ex.Message);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _workDir
        };
        psi.ArgumentList.Add(path);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var sbOut = new System.Text.StringBuilder();
        var sbErr = new System.Text.StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        try
        {
            if (!proc.Start())
                return DiagResult.Fail("Failed to start bash.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                return DiagResult.Fail($"{scriptName} timed out after {Timeout.TotalSeconds:0}s.");
            }

            return new DiagResult
            {
                Ok = proc.ExitCode == 0,
                ExitCode = proc.ExitCode,
                Stdout = sbOut.ToString().TrimEnd(),
                Stderr = sbErr.ToString().TrimEnd(),
                Error = proc.ExitCode == 0 ? null : $"{scriptName} exited {proc.ExitCode}."
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Diagnostic script {Script} failed to run", scriptName);
            return DiagResult.Fail("Run failed: " + ex.Message);
        }
    }

    // Extract the embedded script to a stable temp path, normalize line endings
    // (a CRLF shebang breaks bash), and mark it executable. Overwritten each run so
    // a rebuilt script is always the one that executes.
    private async Task<string> ExtractAsync(string resName, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(_workDir, fileName);
        string body;
        await using (var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName)
                              ?? throw new InvalidOperationException("resource stream null"))
        using (var reader = new StreamReader(rs))
        {
            body = await reader.ReadToEndAsync(ct);
        }
        body = body.Replace("\r\n", "\n");
        await File.WriteAllTextAsync(path, body, ct);
        TryChmodExec(path);
        return path;
    }

    // Best-effort chmod +x. Not fatal — we invoke `bash <path>`, which runs the
    // script with or without the execute bit.
    private void TryChmodExec(string path)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "/bin/chmod", UseShellExecute = false };
            psi.ArgumentList.Add("+x");
            psi.ArgumentList.Add(path);
            Process.Start(psi)?.WaitForExit(2000);
        }
        catch { /* ignore */ }
    }

    private static string? FindResource(string fileNameSuffix)
    {
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        // resource names look like "<RootNamespace>.<folder>.bot_run_report.sh"
        return names.FirstOrDefault(n => n.EndsWith("." + fileNameSuffix, StringComparison.OrdinalIgnoreCase)
                                      || n.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DiagResult
{
    public bool Ok { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public string? Error { get; init; }

    public static DiagResult Fail(string error) => new() { Ok = false, ExitCode = -1, Error = error };
}
