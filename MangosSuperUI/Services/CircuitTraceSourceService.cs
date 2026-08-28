using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MangosSuperUI.Services;

/// <summary>
/// Resolves the two read-only source roots used by the Circuit Board and owns
/// uploaded source packages. Uploaded files live outside wwwroot and are never
/// addressable by path from a browser; the Circuit Board still reads only a
/// server-registered probe site through its confined source reader.
/// </summary>
public sealed class CircuitTraceSourceService
{
    public const string MangosSuperUiRepositoryUrl = "https://github.com/Yafrovon/MangosSuperUI";
    public const string SuperUiCoreRepositoryUrl = "https://github.com/Yafrovon/SuperUI-Core";
    public const int MaxArchiveMegabytes = 256;
    public const long MaxArchiveBytes = (long)MaxArchiveMegabytes * 1024 * 1024;
    internal const long MaxExtractedBytes = 512L * 1024 * 1024;
    internal const long MaxSourceFileBytes = 16L * 1024 * 1024;
    internal const int MaxArchiveEntries = 50_000;

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CircuitTraceSourceService> _logger;
    private readonly SemaphoreSlim _uploadLock = new(1, 1);

    /// <summary>
    /// The .NET SDK appends the repository commit to AssemblyInformationalVersion
    /// for repository builds (for example, 1.0.0+abc123...). Expose that safe,
    /// non-secret identifier so operators can download the recorded C# commit.
    /// </summary>
    public static string? InstalledApplicationRevision { get; } = ReadInstalledApplicationRevision();

    public static string? InstalledApplicationSourceArchiveUrl => InstalledApplicationRevision is { } revision
        ? $"{MangosSuperUiRepositoryUrl}/archive/{revision}.zip"
        : null;

    public CircuitTraceSourceService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<CircuitTraceSourceService> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    private string ManagedRoot
    {
        get
        {
            string applicationDataRoot = DefaultApplicationDataRoot();
            string? configured = _configuration["CircuitTrace:SourcePackageDirectory"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string candidate = Path.IsPathRooted(configured)
                    ? configured
                    : Path.Combine(applicationDataRoot, configured);
                string fullPath = Path.GetFullPath(candidate);

                // Uploaded archives must never become static web content or live
                // inside the publish tree that a deployment may replace.
                if (IsWithin(_environment.ContentRootPath, fullPath))
                    throw new InvalidOperationException(
                        "Uploaded source storage must be outside the published application folder.");
                return fullPath;
            }

            // Keep uploads outside the publish directory so deploying a fresh build
            // cannot erase the operator's source packages.
            return Path.Combine(applicationDataRoot, "CircuitTraceSources");
        }
    }

    private static string DefaultApplicationDataRoot()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = Path.Combine(Path.GetTempPath(), "MangosSuperUI-data");
        return Path.Combine(appData, "MangosSuperUI");
    }

    public CircuitTraceSourceSetupStatus GetStatus()
    {
        CircuitTraceSourceLocationStatus csharp = ResolveCSharp();
        CircuitTraceSourceLocationStatus cpp = ResolveCpp();
        string versionMaterial = string.Join('|',
            VersionMaterial(csharp),
            VersionMaterial(cpp));
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionMaterial)))[..16];
        return new CircuitTraceSourceSetupStatus(csharp.Ready && cpp.Ready, version, csharp, cpp);
    }

    public string? GetCSharpRoot()
    {
        CircuitTraceSourceLocationStatus status = ResolveCSharp();
        return status.Ready ? status.Root : null;
    }

    public string? GetCppRoot()
    {
        CircuitTraceSourceLocationStatus status = ResolveCpp();
        return status.Ready ? status.Root : null;
    }

    public static bool TryParseKind(string? value, out CircuitTraceSourceKind kind)
    {
        if (string.Equals(value, "csharp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "cs", StringComparison.OrdinalIgnoreCase))
        {
            kind = CircuitTraceSourceKind.CSharp;
            return true;
        }

        if (string.Equals(value, "cpp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "c++", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "vmangos", StringComparison.OrdinalIgnoreCase))
        {
            kind = CircuitTraceSourceKind.Cpp;
            return true;
        }

        kind = default;
        return false;
    }

    public async Task<CircuitTraceSourceUploadResult> UploadArchiveAsync(
        CircuitTraceSourceKind kind,
        Stream archiveStream,
        string fileName,
        long suppliedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!string.Equals(Path.GetExtension(fileName), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .zip source package.");
        if (suppliedLength <= 0)
            throw new InvalidDataException("The source package is empty.");
        if (suppliedLength > MaxArchiveBytes)
            throw new InvalidDataException($"The source package is larger than {MaxArchiveMegabytes} MB.");

        // Serialize the whole install, not just the final rename. Otherwise many
        // concurrent uploads can each consume the full extraction allowance and a
        // request can report another request's just-promoted tree as its own.
        await _uploadLock.WaitAsync(cancellationToken);
        string zipPath = string.Empty;
        string staging = string.Empty;
        try
        {
            // IConfiguration reloads in place. Snapshot the managed root so one
            // upload cannot stage under one root and promote under another.
            string managedRoot = ManagedRoot;
            Directory.CreateDirectory(managedRoot);
            string token = Guid.NewGuid().ToString("N");
            zipPath = Path.Combine(managedRoot, $".{KindName(kind)}-{token}.zip");
            staging = Path.Combine(managedRoot, $".{KindName(kind)}-incoming-{token}");
            Directory.CreateDirectory(staging);

            long archiveBytes = await CopyArchiveBoundedAsync(
                archiveStream,
                zipPath,
                cancellationToken);
            int sourceFiles = await ExtractSourceFilesAsync(
                kind,
                zipPath,
                staging,
                cancellationToken);

            string? discovered = kind == CircuitTraceSourceKind.CSharp
                ? FindCSharpRoot(staging)
                : FindCppRoot(staging);
            if (discovered == null)
            {
                string expected = kind == CircuitTraceSourceKind.CSharp
                    ? "MangosSuperUI.csproj with its BotLogic folder"
                    : "the VMaNGOS src/game/SuperUiContent/SuiBots folder";
                throw new InvalidDataException($"That ZIP does not contain {expected}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Promote(kind, discovered, staging, managedRoot);
            CircuitTraceSourceSetupStatus status = GetStatus();
            CircuitTraceSourceLocationStatus location = kind == CircuitTraceSourceKind.CSharp
                ? status.CSharp
                : status.Cpp;
            if (!location.Ready)
                throw new IOException("The source package was extracted but did not become readable.");

            return new CircuitTraceSourceUploadResult(
                kind,
                sourceFiles,
                archiveBytes,
                location,
                status);
        }
        finally
        {
            if (zipPath.Length > 0) TryDeleteFile(zipPath);
            if (staging.Length > 0) TryDeleteDirectory(staging);
            _uploadLock.Release();
        }
    }

    private CircuitTraceSourceLocationStatus ResolveCSharp()
    {
        string configured = _configuration["CircuitTrace:CSharpSourcePath"] ?? string.Empty;
        if (TryConfiguredRoot(configured, FindCSharpRoot, out string? configuredRoot))
        {
            return Ready(
                CircuitTraceSourceKind.CSharp,
                configuredRoot!,
                "configured folder",
                "MangosSuperUI C# source is readable from the configured server folder.");
        }

        string uploaded;
        try
        {
            uploaded = Path.Combine(ManagedRoot, KindName(CircuitTraceSourceKind.CSharp));
        }
        catch (InvalidOperationException ex)
        {
            return Missing(CircuitTraceSourceKind.CSharp, ex.Message);
        }
        if (FindCSharpRoot(uploaded) is { } uploadedRoot)
        {
            return Ready(
                CircuitTraceSourceKind.CSharp,
                uploadedRoot,
                "uploaded package",
                "MangosSuperUI C# source is ready from an uploaded ZIP.");
        }

        // A development run from the project folder already has exact source and
        // should not demand that the developer upload a second copy of it.
        if (FindCSharpRoot(_environment.ContentRootPath) is { } checkoutRoot)
        {
            return Ready(
                CircuitTraceSourceKind.CSharp,
                checkoutRoot,
                "application checkout",
                "MangosSuperUI C# source is available in this development checkout.");
        }

        string message = string.IsNullOrWhiteSpace(configured)
            ? "Choose the MangosSuperUI project folder on this server or upload its source ZIP."
            : "The configured MangosSuperUI source folder is not readable or is not the project root.";
        return Missing(CircuitTraceSourceKind.CSharp, message);
    }

    private CircuitTraceSourceLocationStatus ResolveCpp()
    {
        string configured = _configuration["Vmangos:VmangosSourcePath"] ?? string.Empty;
        if (TryConfiguredRoot(configured, FindCppRoot, out string? configuredRoot))
        {
            return Ready(
                CircuitTraceSourceKind.Cpp,
                configuredRoot!,
                "configured folder",
                "SuperUI-Core C++ source is readable from the configured server folder.");
        }

        string uploaded;
        try
        {
            uploaded = Path.Combine(ManagedRoot, KindName(CircuitTraceSourceKind.Cpp));
        }
        catch (InvalidOperationException ex)
        {
            return Missing(CircuitTraceSourceKind.Cpp, ex.Message);
        }
        if (FindCppRoot(uploaded) is { } uploadedRoot)
        {
            return Ready(
                CircuitTraceSourceKind.Cpp,
                uploadedRoot,
                "uploaded package",
                "SuperUI-Core C++ source is ready from an uploaded ZIP.");
        }

        string message = string.IsNullOrWhiteSpace(configured)
            ? "Choose the SuperUI-Core src folder on this server or upload its source ZIP."
            : "The configured SuperUI-Core source folder is not readable or lacks SuperUiContent/SuiBots.";
        return Missing(CircuitTraceSourceKind.Cpp, message);
    }

    private bool TryConfiguredRoot(
        string configured,
        Func<string, string?> discover,
        out string? root)
    {
        root = null;
        if (string.IsNullOrWhiteSpace(configured)) return false;
        try
        {
            string candidate = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(_environment.ContentRootPath, configured);
            root = discover(Path.GetFullPath(candidate));
            return root != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            _logger.LogDebug(ex, "Circuit source root {Root} is unavailable", configured);
            return false;
        }
    }

    private static CircuitTraceSourceLocationStatus Ready(
        CircuitTraceSourceKind kind,
        string root,
        string origin,
        string message) => new(
            true,
            KindName(kind),
            KindLabel(kind),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
            origin,
            message);

    private static CircuitTraceSourceLocationStatus Missing(
        CircuitTraceSourceKind kind,
        string message) => new(
            false,
            KindName(kind),
            KindLabel(kind),
            null,
            "missing",
            message);

    private static string? FindCSharpRoot(string startingPath)
    {
        if (!Directory.Exists(startingPath)) return null;

        foreach (string candidate in CandidateDirectories(startingPath, "MangosSuperUI"))
        {
            if (File.Exists(Path.Combine(candidate, "MangosSuperUI.csproj"))
                && Directory.Exists(Path.Combine(candidate, "BotLogic")))
                return Path.GetFullPath(candidate);
        }

        try
        {
            return Directory.EnumerateFiles(
                    startingPath,
                    "MangosSuperUI.csproj",
                    SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => path != null && Directory.Exists(Path.Combine(path, "BotLogic")))
                .OrderBy(path => PathDepth(startingPath, path!))
                .ThenBy(path => path, PathComparer)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindCppRoot(string startingPath)
    {
        if (!Directory.Exists(startingPath)) return null;

        foreach (string candidate in CandidateDirectories(startingPath, "src"))
        {
            if (HasSuiBotsSource(candidate)) return Path.GetFullPath(candidate);
        }

        try
        {
            return Directory.EnumerateDirectories(
                    startingPath,
                    "SuiBots",
                    SearchOption.AllDirectories)
                .Select(path => new DirectoryInfo(path))
                .Where(dir => dir.Parent?.Name.Equals("SuperUiContent", StringComparison.OrdinalIgnoreCase) == true
                    && dir.Parent.Parent?.Name.Equals("game", StringComparison.OrdinalIgnoreCase) == true
                    && Directory.EnumerateFiles(dir.FullName, "*.cpp", SearchOption.AllDirectories).Any())
                .Select(dir => dir.Parent!.Parent!.Parent!.FullName)
                .OrderBy(path => PathDepth(startingPath, path))
                .ThenBy(path => path, PathComparer)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidateDirectories(string root, string childName)
    {
        yield return root;
        yield return Path.Combine(root, childName);
    }

    private static bool HasSuiBotsSource(string root)
    {
        string path = Path.Combine(root, "game", "SuperUiContent", "SuiBots");
        try
        {
            return Directory.Exists(path)
                && Directory.EnumerateFiles(path, "*.cpp", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int PathDepth(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Length;

    private static async Task<long> CopyArchiveBoundedAsync(
        Stream input,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            useAsync: true);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxArchiveBytes)
                throw new InvalidDataException($"The source package is larger than {MaxArchiveMegabytes} MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }

    private static async Task<int> ExtractSourceFilesAsync(
        CircuitTraceSourceKind kind,
        string zipPath,
        string staging,
        CancellationToken cancellationToken)
    {
        using var file = File.OpenRead(zipPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException($"The source package has more than {MaxArchiveEntries:N0} entries.");

        long extractedBytes = 0;
        int sourceFiles = 0;
        var destinations = new HashSet<string>(PathComparer);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!TryNormalizeArchivePath(entry.FullName, out string relative))
                throw new InvalidDataException("The source package contains an unsafe path.");
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            int windowsAttributes = entry.ExternalAttributes & 0xFFFF;
            if (unixType != 0 && unixType is not 0x4000 and not 0x8000)
                throw new InvalidDataException("The source package contains a link or special file.");
            if ((windowsAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The source package contains a link or special file.");

            bool isDirectory = unixType == 0x4000
                || (windowsAttributes & (int)FileAttributes.Directory) != 0
                || entry.FullName.EndsWith('/')
                || entry.FullName.EndsWith('\\');
            if (relative.Length == 0 || isDirectory)
                continue;
            if (!ShouldExtract(kind, relative)) continue;
            if (entry.Length > MaxSourceFileBytes)
                throw new InvalidDataException($"Source file '{Path.GetFileName(relative)}' is unexpectedly large.");
            if (extractedBytes + entry.Length > MaxExtractedBytes)
                throw new InvalidDataException("The extracted source package would be larger than 512 MB.");

            string destination = Path.GetFullPath(Path.Combine(
                staging,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(staging, destination) || !destinations.Add(destination))
                throw new InvalidDataException("The source package contains duplicate or unsafe paths.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using Stream source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true);
            long copied = await CopyEntryBoundedAsync(source, target, cancellationToken);
            extractedBytes += copied;
            if (extractedBytes > MaxExtractedBytes)
                throw new InvalidDataException("The extracted source package would be larger than 512 MB.");
            if (IsSourceExtension(kind, relative)) sourceFiles++;
        }

        if (sourceFiles == 0)
            throw new InvalidDataException("The ZIP does not contain source files for that package type.");
        return sourceFiles;
    }

    private static async Task<long> CopyEntryBoundedAsync(
        Stream source,
        Stream target,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            copied += read;
            if (copied > MaxSourceFileBytes)
                throw new InvalidDataException("A source file expands beyond the per-file safety limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return copied;
    }

    private static void Promote(
        CircuitTraceSourceKind kind,
        string discoveredRoot,
        string staging,
        string managedRoot)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(discoveredRoot));
        string stageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
        if (!Directory.Exists(source) || !IsWithin(stageRoot, source))
            throw new InvalidDataException("The discovered source root escaped its staging directory.");

        // Write all fallible package content before touching the active tree. Once
        // source is moved into place, there is no follow-up write that can force us
        // to delete a successfully promoted tree during rollback.
        File.WriteAllText(
            Path.Combine(source, ".circuit-source-version"),
            Guid.NewGuid().ToString("N"));

        string final = Path.Combine(managedRoot, KindName(kind));
        string backup = Path.Combine(
            managedRoot,
            $".{KindName(kind)}-backup-{Guid.NewGuid():N}");
        bool hadPrevious = Directory.Exists(final);
        if (hadPrevious) Directory.Move(final, backup);

        try
        {
            Directory.Move(source, final);
        }
        catch
        {
            if (hadPrevious && Directory.Exists(backup) && !Directory.Exists(final))
                Directory.Move(backup, final);
            throw;
        }

        if (hadPrevious) TryDeleteDirectory(backup);
    }

    private static bool ShouldExtract(CircuitTraceSourceKind kind, string path) =>
        IsSourceExtension(kind, path)
        || (kind == CircuitTraceSourceKind.CSharp
            && Path.GetFileName(path).Equals("MangosSuperUI.csproj", StringComparison.OrdinalIgnoreCase));

    private static bool IsSourceExtension(CircuitTraceSourceKind kind, string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return kind == CircuitTraceSourceKind.CSharp
            ? extension == ".cs"
            : extension is ".c" or ".cc" or ".cpp" or ".cxx"
                or ".h" or ".hh" or ".hpp" or ".hxx" or ".inl";
    }

    private static bool TryNormalizeArchivePath(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return false;
        string candidate = path.Replace('\\', '/');
        if (candidate.Length > 512) return false;
        if (candidate.StartsWith('/') || candidate.Contains(':')) return false;
        string[] pieces = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Any(piece => piece is "." or "..")) return false;
        normalized = string.Join('/', pieces);
        return true;
    }

    private static bool IsWithin(string root, string candidate)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string KindName(CircuitTraceSourceKind kind) =>
        kind == CircuitTraceSourceKind.CSharp ? "csharp" : "cpp";

    private static string KindLabel(CircuitTraceSourceKind kind) =>
        kind == CircuitTraceSourceKind.CSharp
            ? "MangosSuperUI C# source"
            : "SuperUI-Core C++ source (VMaNGOS fork)";

    private static string? ReadInstalledApplicationRevision()
    {
        string? version = typeof(CircuitTraceSourceService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        int separator = version?.LastIndexOf('+') ?? -1;
        if (separator < 0 || separator == version!.Length - 1) return null;

        string candidate = version[(separator + 1)..].Trim();
        return candidate.Length is >= 7 and <= 64
            && candidate.All(Uri.IsHexDigit)
                ? candidate.ToLowerInvariant()
                : null;
    }

    private static string VersionMaterial(CircuitTraceSourceLocationStatus source)
    {
        if (!source.Ready || source.Root == null) return source.Kind + ":missing";
        string marker = Path.Combine(source.Root, ".circuit-source-version");
        string token = string.Empty;
        try
        {
            if (File.Exists(marker)) token = File.ReadAllText(marker).Trim();
            else
            {
                string projectMarker = source.Kind == "csharp"
                    ? Path.Combine(source.Root, "MangosSuperUI.csproj")
                    : Path.Combine(source.Root, "game", "SuperUiContent", "SuiBots");
                token = File.GetLastWriteTimeUtc(projectMarker).Ticks.ToString();
            }
        }
        catch
        {
            token = "unreadable-version";
        }

        return string.Join(':', source.Kind, source.Origin, source.Root, token);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A failed cleanup must not replace the useful upload error.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A failed cleanup must not replace the useful upload error.
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

public enum CircuitTraceSourceKind
{
    CSharp,
    Cpp
}

public sealed record CircuitTraceSourceLocationStatus(
    bool Ready,
    string Kind,
    string Label,
    string? Root,
    string Origin,
    string Message);

public sealed record CircuitTraceSourceSetupStatus(
    bool Ready,
    string SourceVersion,
    CircuitTraceSourceLocationStatus CSharp,
    CircuitTraceSourceLocationStatus Cpp);

public sealed record CircuitTraceSourceUploadResult(
    CircuitTraceSourceKind Kind,
    int SourceFileCount,
    long ArchiveBytes,
    CircuitTraceSourceLocationStatus Source,
    CircuitTraceSourceSetupStatus Status);
