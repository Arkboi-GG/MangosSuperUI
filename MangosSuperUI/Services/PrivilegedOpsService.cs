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
    long? RunningHard,
    string Source)
{
    /// <summary>True when the running process still has less headroom than the drop-in asks for.</summary>
    public bool RestartRequired =>
        DropInPresent && DropInValue is long want && RunningSoft is long have && have < want;

    /// <summary>
    /// The limit is too low to be worth leaving alone. Every bot holds one bridge
    /// socket in this process, so the soft limit is a hard cap on fleet size.
    /// </summary>
    public bool LimitTooLow =>
        RunningSoft is long soft && soft < PrivilegedOpsService.WarnBelowNoFile;

    /// <summary>Roughly how many bots fit before accept/connect starts failing.</summary>
    public long? ApproximateBotCeiling =>
        RunningSoft is long soft ? Math.Max(0, soft - PrivilegedOpsService.NonBridgeDescriptorAllowance) : null;
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

    /// <summary>Warn below this. systemd's default of 1024 sits under it.</summary>
    public const long WarnBelowNoFile = 8_192;

    /// <summary>
    /// Descriptors the world process holds for things that are not bot bridge
    /// sockets — logs, DB handles, listeners, tty. Measured at ~36 on a live
    /// 692-bot server; rounded up so the estimated ceiling errs low.
    /// </summary>
    public const long NonBridgeDescriptorAllowance = 64;

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

    /// <param name="knownPid">
    /// The running process, from ProcessManagerService. Supplying it lets the
    /// report work with NO privilege at all, which matters more than it sounds:
    /// an install that has never run the setup script has no helper, and those
    /// are exactly the installs still capped at 1024 descriptors. Detection must
    /// never depend on the thing being detected as missing.
    /// </param>
    public async Task<UnitLimitsReport> GetLimitsAsync(string unit, int? knownPid = null, CancellationToken ct = default)
    {
        if (!IsAllowedUnit(unit))
            return Unavailable(unit, $"unit not allowed: {unit}");

        if (!HelperInstalled)
            return ReadFromProc(unit, knownPid, $"privileged helper not installed at {_helperPath}");

        PrivilegedOpResult result = await RunAsync(new[] { "show-limits", unit }, ct);
        if (!result.Ok)
            return ReadFromProc(unit, knownPid, result.Error);

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
            RunningHard: ParseLong(kv.GetValueOrDefault("running_hard")),
            Source: "helper");
    }

    /// <summary>
    /// Unprivileged fallback: /proc/&lt;pid&gt;/limits is world-readable, so the
    /// limit can always be reported even when nothing can change it. The drop-in
    /// and systemd-configured values stay unknown here — only the running
    /// process is observable — and the running value is the one that matters.
    /// </summary>
    internal static UnitLimitsReport ReadFromProc(string unit, int? pid, string? helperError)
    {
        if (!OperatingSystem.IsLinux() || pid is not int id || id <= 0)
            return Unavailable(unit, helperError ?? "no running process");

        try
        {
            string path = $"/proc/{id}/limits";
            if (!File.Exists(path))
                return Unavailable(unit, helperError ?? $"{path} not readable");

            foreach (string line in File.ReadLines(path))
            {
                if (!line.StartsWith("Max open files", StringComparison.Ordinal))
                    continue;

                string[] f = line["Max open files".Length..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 2)
                    break;

                return new UnitLimitsReport(
                    Available: true,
                    Error: helperError,
                    Unit: unit,
                    DropInPresent: false,
                    DropInValue: null,
                    ConfiguredSoft: null,
                    ConfiguredHard: null,
                    RunningPid: id,
                    RunningSoft: ParseLimit(f[0]),
                    RunningHard: ParseLimit(f[1]),
                    Source: "proc");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return Unavailable(unit, helperError ?? "could not read process limits");
    }

    /// <summary>/proc renders an absent limit as the word "unlimited".</summary>
    private static long? ParseLimit(string text)
        => string.Equals(text, "unlimited", StringComparison.OrdinalIgnoreCase)
            ? long.MaxValue
            : ParseLong(text);

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
        => new(false, error, unit, false, null, null, null, 0, null, null, "none");
}
