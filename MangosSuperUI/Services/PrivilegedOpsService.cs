using System.Diagnostics;
using System.Globalization;

namespace MangosSuperUI.Services;

/// <summary>
/// What the helper reports about one unit's descriptor limit.
/// </summary>
/// <param name="RunningSoft">
/// The limit of the PROCESS, which is the only thing that matters. A drop-in on
/// disk proves nothing until the unit restarts, so a report where
/// <see cref="DropInPresent"/> is true but RunningSoft is still 1024 means
/// exactly one thing: it has not been restarted yet.
/// </param>
public sealed record UnitLimitsReport(
    bool Available,
    string? Error,
    string Unit,
    bool DropInPresent,
    long? DropInValue,
    long? ConfiguredSoft,
    long? ConfiguredHard,
    int RunningPid,
    long? RunningSoft,
    long? RunningHard)
{
    /// <summary>True when the running process still has less headroom than the drop-in asks for.</summary>
    public bool RestartRequired =>
        DropInPresent && DropInValue is long want && RunningSoft is long have && have < want;
}

public sealed record PrivilegedOpResult(bool Ok, string Output, string? Error);

/// <summary>
/// Invokes the one root-owned helper SuperUI is permitted to run
/// (<c>Scripts/superui-privileged.sh</c>, behind a narrow NOPASSWD sudoers
/// grant). Everything privileged funnels through here so the trust boundary is
/// a single reviewable file rather than scattered shell-outs.
///
/// The helper validates its own arguments as well; this class does not rely on
/// callers being careful, and neither does the script.
/// </summary>
public sealed class PrivilegedOpsService
{
    /// <summary>Mirrors the helper's allowlist. Kept in sync deliberately —
    /// a unit rejected here never reaches sudo at all.</summary>
    private static readonly string[] AllowedUnits =
        { "mangosd", "realmd", "cmangos-mangosd", "cmangos-realmd" };

    internal const long MinimumNoFile = 1024;
    internal const long MaximumNoFile = 1_048_576;

    /// <summary>
    /// Covers the 10k target with wide margin and stays well under systemd's
    /// 524288 default hard limit. Deliberately not "infinity": on hosts where
    /// the hard limit is 2^30, code that sizes arrays by descriptor count
    /// behaves badly.
    /// </summary>
    public const long RecommendedNoFile = 65_535;

    /// <summary>
    /// Where setup installs the root-owned helper. Deliberately OUTSIDE the app
    /// directory: the service user can write its own install tree during a
    /// deploy, and a file that user can rewrite must never be the file sudo runs
    /// as root. The copy shipped under <c>Scripts/</c> is only the source for
    /// installation — pointing at it would be the vulnerability this avoids.
    /// </summary>
    private const string InstalledHelperPath = "/usr/local/lib/mangossuperui/superui-privileged.sh";

    private readonly string _helperPath;
    private readonly ILogger<PrivilegedOpsService> _logger;

    public PrivilegedOpsService(IConfiguration config, ILogger<PrivilegedOpsService> logger)
    {
        _logger = logger;
        _helperPath = config["SuperUI:PrivilegedHelperPath"] ?? InstalledHelperPath;
    }

    public static bool IsAllowedUnit(string unit) =>
        AllowedUnits.Contains(unit, StringComparer.Ordinal);

    public bool HelperInstalled => OperatingSystem.IsLinux() && File.Exists(_helperPath);

    public async Task<UnitLimitsReport> GetLimitsAsync(string unit, CancellationToken ct = default)
    {
        if (!IsAllowedUnit(unit))
            return Unavailable(unit, $"unit not allowed: {unit}");
        if (!HelperInstalled)
            return Unavailable(unit, $"privileged helper not installed at {_helperPath}");

        PrivilegedOpResult result = await RunAsync(new[] { "show-limits", unit }, ct);
        if (!result.Ok)
            return Unavailable(unit, result.Error);

        Dictionary<string, string> kv = ParseKeyValues(result.Output);
        return new UnitLimitsReport(
            Available: true,
            Error: null,
            Unit: unit,
            DropInPresent: kv.GetValueOrDefault("dropin_present") == "1",
            DropInValue: ParseLong(kv.GetValueOrDefault("dropin_LimitNOFILE")),
            ConfiguredSoft: ParseLong(kv.GetValueOrDefault("configured_LimitNOFILESoft")),
            ConfiguredHard: ParseLong(kv.GetValueOrDefault("configured_LimitNOFILE")),
            RunningPid: (int)(ParseLong(kv.GetValueOrDefault("running_pid")) ?? 0),
            RunningSoft: ParseLong(kv.GetValueOrDefault("running_soft")),
            RunningHard: ParseLong(kv.GetValueOrDefault("running_hard")));
    }

    /// <summary>
    /// Writes the drop-in. Does not restart: applying a limit and bouncing a
    /// live world server are separate decisions, and the caller already has a
    /// restart control.
    /// </summary>
    public async Task<PrivilegedOpResult> SetNoFileAsync(string unit, long value, CancellationToken ct = default)
    {
        if (!IsAllowedUnit(unit))
            return new PrivilegedOpResult(false, "", $"unit not allowed: {unit}");
        if (value < MinimumNoFile || value > MaximumNoFile)
            return new PrivilegedOpResult(false, "", $"value must be between {MinimumNoFile} and {MaximumNoFile}");
        if (!HelperInstalled)
            return new PrivilegedOpResult(false, "", $"privileged helper not installed at {_helperPath}");

        _logger.LogInformation("Setting LimitNOFILE={Value} for {Unit}", value, unit);
        return await RunAsync(
            new[] { "set-nofile", unit, value.ToString(CultureInfo.InvariantCulture) },
            ct);
    }

    private async Task<PrivilegedOpResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -n: never prompt. Without it a missing sudoers grant hangs the request
        // waiting on a password nobody can type.
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(_helperPath);
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using Process? proc = Process.Start(psi);
            if (proc is null)
                return new PrivilegedOpResult(false, "", "could not start sudo");

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                string error = string.IsNullOrWhiteSpace(stderr) ? $"exit {proc.ExitCode}" : stderr.Trim();
                _logger.LogWarning("Privileged helper failed: {Error}", error);
                return new PrivilegedOpResult(false, stdout, error);
            }

            return new PrivilegedOpResult(true, stdout, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Privileged helper invocation failed");
            return new PrivilegedOpResult(false, "", ex.Message);
        }
    }

    internal static Dictionary<string, string> ParseKeyValues(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    private static long? ParseLong(string? text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    private static UnitLimitsReport Unavailable(string unit, string? error)
        => new(false, error, unit, false, null, null, null, 0, null, null);
}
