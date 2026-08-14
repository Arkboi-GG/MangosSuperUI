using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Pure file/archive operations used by World State. It never talks to MySQL
/// and never controls a process, which keeps preflight and RTS world creation
/// safe to run while another world remains physically materialized.
/// </summary>
public sealed class WorldArtifactService
{
    public const string WorldMangos = "world_mangos.sql.gz";
    public const string WorldAdmin = "world_vmangos_admin.sql.gz";
    public const string PlayersCharacters = "players_characters.sql.gz";
    public const string PlayersCharactersSchema = "players_characters_schema.sql.gz";
    public const string PlayersCharactersSystem = "players_characters_system.sql.gz";
    public const string PlayersRealmd = "players_realmd.sql.gz";
    public const string CoreArchive = "core_source.tar.gz";
    public const string CoreConfig = "core_mangosd.conf";
    public const string RotationAssignments = "bot_rotation_assignments.json";

    public static readonly (string Id, string Group, string File, string Format, bool Required)[] V2Artifacts =
    {
        ("world-mangos", "world", WorldMangos, "sql+gzip", true),
        ("world-admin", "world", WorldAdmin, "sql+gzip", true),
        ("players-characters", "players", PlayersCharacters, "sql+gzip", true),
        ("players-characters-schema", "players", PlayersCharactersSchema, "sql+gzip", true),
        ("players-characters-system", "players", PlayersCharactersSystem, "sql+gzip", true),
        ("players-realmd", "players", PlayersRealmd, "sql+gzip", true),
        ("core-source", "core", CoreArchive, "tar+gzip", true),
        ("core-config", "core", CoreConfig, "text", true),
        ("rotation-assignments", "core", RotationAssignments, "json", false)
    };

    /// <summary>
    /// Validates the manifest metadata itself before any artifact is trusted. A v2
    /// entry cannot weaken a required canonical artifact by relabelling it optional,
    /// omitting its checksum, or changing the format used by structural validation.
    /// </summary>
    public static IReadOnlyList<string> ValidateV2ArtifactMetadata(
        IReadOnlyCollection<SnapshotArtifact> artifacts,
        string sourceName)
    {
        var errors = new List<string>();
        var byFile = artifacts
            .GroupBy(artifact => artifact.FileName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var duplicate in byFile.Where(group => group.Count() > 1))
            errors.Add($"{sourceName} contains duplicate artifact filename '{duplicate.Key}'.");

        foreach (var duplicate in artifacts
                     .GroupBy(artifact => artifact.Id ?? "", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            errors.Add($"{sourceName} contains duplicate artifact id '{duplicate.Key}'.");

        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.FileName) || Path.GetFileName(artifact.FileName) != artifact.FileName)
                errors.Add($"{sourceName} contains an unsafe artifact filename '{artifact.FileName}'.");
            if (artifact.Length <= 0)
                errors.Add($"{sourceName} artifact '{artifact.FileName}' has no positive byte length.");
            if (!IsSha256(artifact.Sha256))
                errors.Add($"{sourceName} artifact '{artifact.FileName}' has no valid SHA-256 checksum.");
            if (string.IsNullOrWhiteSpace(artifact.Format))
                errors.Add($"{sourceName} artifact '{artifact.FileName}' has no format.");
        }

        foreach (var definition in V2Artifacts)
        {
            var matches = artifacts
                .Where(artifact => string.Equals(artifact.FileName, definition.File, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                if (definition.Required)
                    errors.Add($"{sourceName} is missing required artifact '{definition.File}'.");
                continue;
            }

            var artifact = matches[0];
            if (!string.Equals(artifact.FileName, definition.File, StringComparison.Ordinal) ||
                !string.Equals(artifact.Id, definition.Id, StringComparison.Ordinal) ||
                !string.Equals(artifact.Group, definition.Group, StringComparison.Ordinal) ||
                !string.Equals(artifact.Format, definition.Format, StringComparison.Ordinal) ||
                artifact.Required != definition.Required)
            {
                errors.Add(
                    $"{sourceName} artifact '{definition.File}' does not match its canonical " +
                    $"filename/id/group/format/required metadata.");
            }
        }

        return errors;
    }

    /// <summary>
    /// The disk manifest and worlds.json are independent copies of the integrity
    /// metadata. Requiring exact agreement prevents either copy from silently
    /// downgrading the validation applied to a snapshot.
    /// </summary>
    public static IReadOnlyList<string> CompareV2ArtifactMetadata(
        IReadOnlyCollection<SnapshotArtifact> registryArtifacts,
        IReadOnlyCollection<SnapshotArtifact> diskArtifacts)
    {
        var errors = new List<string>();
        var registry = registryArtifacts
            .GroupBy(artifact => artifact.FileName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var disk = diskArtifacts
            .GroupBy(artifact => artifact.FileName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in registry.Keys.Union(disk.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!registry.TryGetValue(fileName, out var registryArtifact) ||
                !disk.TryGetValue(fileName, out var diskArtifact))
            {
                errors.Add($"Artifact '{fileName}' is not represented in both worlds.json and manifest.json.");
                continue;
            }

            if (!ArtifactMetadataEquals(registryArtifact, diskArtifact))
                errors.Add($"Artifact '{fileName}' metadata differs between worlds.json and manifest.json.");
        }

        return errors;
    }

    private static bool ArtifactMetadataEquals(SnapshotArtifact left, SnapshotArtifact right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        string.Equals(left.Group, right.Group, StringComparison.Ordinal) &&
        string.Equals(left.FileName, right.FileName, StringComparison.Ordinal) &&
        string.Equals(left.Format, right.Format, StringComparison.Ordinal) &&
        left.Length == right.Length &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) &&
        left.Required == right.Required;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public async Task<List<SnapshotArtifact>> DescribeV2ArtifactsAsync(
        string directory, CancellationToken cancellationToken = default)
    {
        var result = new List<SnapshotArtifact>();
        foreach (var definition in V2Artifacts)
        {
            var path = ResolveChild(directory, definition.File);
            if (!File.Exists(path))
            {
                if (definition.Required)
                    throw new FileNotFoundException($"Required snapshot artifact '{definition.File}' is missing.", path);
                continue;
            }
            var info = new FileInfo(path);
            result.Add(new SnapshotArtifact
            {
                Id = definition.Id,
                Group = definition.Group,
                FileName = definition.File,
                Format = definition.Format,
                Length = info.Length,
                Sha256 = await Sha256Async(path, cancellationToken),
                Required = definition.Required
            });
        }
        return result;
    }

    public async Task<SnapshotArtifactCheck> ValidateArtifactAsync(
        string directory, SnapshotArtifact artifact, CancellationToken cancellationToken = default)
    {
        var check = new SnapshotArtifactCheck { Id = artifact.Id, FileName = artifact.FileName };
        try
        {
            var path = ResolveChild(directory, artifact.FileName);
            check.Present = File.Exists(path);
            if (!check.Present)
            {
                // Optional means the artifact may be omitted from the manifest. Once an
                // entry exists (and therefore commits a length/hash), its file must exist.
                check.Valid = false;
                check.Detail = artifact.Required ? "required file is missing" : "listed optional file is missing";
                return check;
            }
            var info = new FileInfo(path);
            if (artifact.Length > 0 && info.Length != artifact.Length)
                throw new InvalidDataException($"length mismatch: expected {artifact.Length}, found {info.Length}");
            if (!string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                var actual = await Sha256Async(path, cancellationToken);
                if (!string.Equals(actual, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch");
            }

            if (artifact.Format.Contains("tar", StringComparison.OrdinalIgnoreCase))
                await ValidateTarGzipAsync(path, cancellationToken);
            else if (artifact.Format.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                await ValidateGzipAsync(path, cancellationToken);
            else if (string.Equals(artifact.Format, "json", StringComparison.OrdinalIgnoreCase))
                _ = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));

            check.Valid = true;
            check.Detail = $"{info.Length:N0} bytes verified";
        }
        catch (Exception ex)
        {
            check.Valid = false;
            check.Detail = ex.Message;
        }
        return check;
    }

    public async Task ValidateGzipAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        await gzip.CopyToAsync(Stream.Null, cancellationToken);
    }

    public async Task ValidateTarGzipAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            EnsureSafeArchiveEntry(entry);
            if (entry.DataStream != null)
                await entry.DataStream.CopyToAsync(Stream.Null, cancellationToken);
        }
    }

    public async Task<string> ExtractLegacyConfigToTempAsync(
        string archivePath, string tempDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(tempDirectory);
        var matches = new List<byte[]>();
        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            EnsureSafeArchiveEntry(entry);
            var normalized = NormalizeArchivePath(entry.Name);
            if (string.Equals(normalized, "mangosd.conf", StringComparison.OrdinalIgnoreCase) && entry.DataStream != null)
            {
                await using var memory = new MemoryStream();
                await entry.DataStream.CopyToAsync(memory, cancellationToken);
                matches.Add(memory.ToArray());
            }
            else if (entry.DataStream != null)
            {
                await entry.DataStream.CopyToAsync(Stream.Null, cancellationToken);
            }
        }
        if (matches.Count != 1)
            throw new InvalidDataException(matches.Count == 0
                ? "Legacy core archive does not contain a root mangosd.conf."
                : "Legacy core archive contains more than one root mangosd.conf.");
        var output = Path.Combine(tempDirectory, $"legacy-mangosd-{Guid.NewGuid():N}.conf");
        await File.WriteAllBytesAsync(output, matches[0], cancellationToken);
        return output;
    }

    /// <summary>
    /// Restores the file-only core group without ever overlaying an existing source tree.
    /// The archive is first expanded into clean sibling directories. Source, SQL, exact-path
    /// config, and rotation assignments are then replaced as one transaction; original paths
    /// remain available as sibling backups until every replacement and verification succeeds.
    /// </summary>
    public async Task<CoreArtifactRestoreResult> RestoreCoreArtifactsAsync(
        string archivePath,
        string sourceRoot,
        string sqlRoot,
        string? configSidecarPath,
        string configTargetPath,
        string? rotationSidecarPath,
        string rotationTargetPath,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeOwnedDirectory(sourceRoot, nameof(sourceRoot));
        var sql = NormalizeOwnedDirectory(sqlRoot, nameof(sqlRoot));
        EnsureIndependentPaths(source, sql, "Source and SQL roots must be separate, non-nested directories.");

        var configTarget = Path.GetFullPath(configTargetPath);
        var rotationTarget = Path.GetFullPath(rotationTargetPath);
        if (string.Equals(configTarget, rotationTarget, PathComparison))
            throw new InvalidOperationException("Core config and rotation assignment targets must be different files.");
        EnsureFileOutsideOwnedTrees(configTarget, source, sql, "mangosd.conf");
        EnsureFileOutsideOwnedTrees(rotationTarget, source, sql, "rotation assignments");

        var operationId = Guid.NewGuid().ToString("N");
        var sourceStage = SiblingWorkPath(source, "stage", operationId);
        var sqlStage = SiblingWorkPath(sql, "stage", operationId);
        var configStage = SiblingWorkPath(configTarget, "stage", operationId);
        var rotationStage = SiblingWorkPath(rotationTarget, "stage", operationId);
        var legacy = string.IsNullOrWhiteSpace(configSidecarPath) || !File.Exists(configSidecarPath);
        PathReplacementTransaction? transaction = null;

        try
        {
            var extracted = await ExtractCoreArchiveAsync(
                archivePath,
                sourceStage,
                sqlStage,
                Path.GetFileName(source),
                Path.GetFileName(sql),
                captureLegacyConfig: legacy,
                cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(configStage)!);
            if (!legacy)
            {
                await CopyNewFileAsync(configSidecarPath!, configStage, cancellationToken);
            }
            else
            {
                if (extracted.LegacyConfig == null)
                    throw new InvalidDataException("Legacy core archive does not contain one root mangosd.conf.");
                await File.WriteAllBytesAsync(configStage, extracted.LegacyConfig, cancellationToken);
            }
            PreserveExistingUnixMode(configTarget, configStage);
            var parsedConfig = await MangosdConfigDocument.LoadAsync(configStage, cancellationToken);
            _ = parsedConfig.GetInt("PlayerLimit");

            Directory.CreateDirectory(Path.GetDirectoryName(rotationStage)!);
            if (!string.IsNullOrWhiteSpace(rotationSidecarPath) && File.Exists(rotationSidecarPath))
                await CopyNewFileAsync(rotationSidecarPath, rotationStage, cancellationToken);
            else
                await File.WriteAllTextAsync(rotationStage, "{}\n", new UTF8Encoding(false), cancellationToken);
            PreserveExistingUnixMode(rotationTarget, rotationStage);
            using (var assignments = System.Text.Json.JsonDocument.Parse(
                       await File.ReadAllTextAsync(rotationStage, cancellationToken)))
            {
                if (assignments.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    throw new InvalidDataException("Rotation assignments must be a JSON object.");
            }

            var expectedConfigHash = await Sha256Async(configStage, cancellationToken);
            transaction = new PathReplacementTransaction(operationId);
            transaction.ReplaceDirectory(sourceStage, source);
            transaction.ReplaceDirectory(sqlStage, sql);
            transaction.ReplaceFile(configStage, configTarget);
            transaction.ReplaceFile(rotationStage, rotationTarget);

            if (!Directory.Exists(source) || !Directory.Exists(sql))
                throw new InvalidDataException("Core directory replacement verification failed.");
            var actualConfigHash = await Sha256Async(configTarget, cancellationToken);
            if (!string.Equals(expectedConfigHash, actualConfigHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Restored mangosd.conf failed SHA-256 verification.");

            transaction.Commit();
            transaction = null;
            return new CoreArtifactRestoreResult
            {
                LegacyConfig = legacy,
                SourceFiles = extracted.SourceFiles,
                SqlFiles = extracted.SqlFiles
            };
        }
        catch (Exception restoreError)
        {
            if (transaction != null)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Core restore failed and the original core paths could not be fully rolled back.",
                        restoreError, rollbackError);
                }
            }
            throw;
        }
        finally
        {
            DeleteDirectoryIfPresent(sourceStage);
            DeleteDirectoryIfPresent(sqlStage);
            DeleteFileIfPresent(configStage);
            DeleteFileIfPresent(rotationStage);
        }
    }

    public async Task ComposeGzipAsync(
        string outputPath, IEnumerable<string> gzipInputs, string postlude,
        CancellationToken cancellationToken = default) =>
        await ComposeGzipWithPreludeAsync(outputPath, gzipInputs, "", postlude, cancellationToken);

    public async Task ComposeGzipWithPreludeAsync(
        string outputPath, IEnumerable<string> gzipInputs, string prelude, string postlude,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var outputFile = File.Create(outputPath);
        await using var output = new GZipStream(outputFile, CompressionLevel.SmallestSize, leaveOpen: false);
        if (!string.IsNullOrWhiteSpace(prelude))
        {
            var bytes = Encoding.UTF8.GetBytes(prelude.Trim() + "\n\n");
            await output.WriteAsync(bytes, cancellationToken);
        }
        foreach (var inputPath in gzipInputs)
        {
            await using var inputFile = File.OpenRead(inputPath);
            await using var input = new GZipStream(inputFile, CompressionMode.Decompress, leaveOpen: false);
            await input.CopyToAsync(output, cancellationToken);
            await output.WriteAsync("\n"u8.ToArray(), cancellationToken);
        }
        if (!string.IsNullOrEmpty(postlude))
        {
            var bytes = Encoding.UTF8.GetBytes("\n" + postlude.Trim() + "\n");
            await output.WriteAsync(bytes, cancellationToken);
        }
    }

    public async Task<DatabaseDumpDefinition?> InspectDatabaseDumpAsync(
        string gzipPath, string expectedDatabase, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedDatabase) || expectedDatabase.Any(c => c is '`' or '\r' or '\n'))
            throw new ArgumentException("Invalid expected database name.", nameof(expectedDatabase));

        await using var file = File.OpenRead(gzipPath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var buffer = new char[512 * 1024];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        var prefix = new string(buffer, 0, length);
        var databaseToken = Regex.Escape($"`{expectedDatabase}`");
        var create = Regex.Match(prefix,
            $@"CREATE\s+DATABASE\b[\s\S]{{0,4096}}?{databaseToken}[\s\S]{{0,4096}}?;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!create.Success) return null;
        var use = Regex.Match(prefix,
            $@"USE\s+{databaseToken}\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!use.Success) return null;

        var charset = Regex.Match(create.Value,
            @"DEFAULT\s+CHARACTER\s+SET\s*(?:=\s*)?`?(?<value>[A-Za-z0-9_]+)`?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var collation = Regex.Match(create.Value,
            @"COLLATE\s*(?:=\s*)?`?(?<value>[A-Za-z0-9_]+)`?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return new DatabaseDumpDefinition
        {
            DatabaseName = expectedDatabase,
            CreateStatement = create.Value.Trim(),
            UseStatement = use.Value.Trim(),
            CharacterSet = charset.Success ? charset.Groups["value"].Value : null,
            Collation = collation.Success ? collation.Groups["value"].Value : null
        };
    }

    public static string BuildDatabasePreamble(DatabaseDumpDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.DatabaseName) ||
            definition.DatabaseName.Any(c => c is '`' or '\r' or '\n') ||
            string.IsNullOrWhiteSpace(definition.CreateStatement) ||
            string.IsNullOrWhiteSpace(definition.UseStatement))
            throw new InvalidDataException("Database dump definition is incomplete.");
        return $"DROP DATABASE IF EXISTS `{definition.DatabaseName}`;\n" +
               definition.CreateStatement.Trim() + "\n" + definition.UseStatement.Trim() + "\n";
    }

    public Task TransformGzipAsync(
        string inputPath, string outputPath, string postlude, CancellationToken cancellationToken = default) =>
        ComposeGzipAsync(outputPath, new[] { inputPath }, postlude, cancellationToken);

    public async Task WriteGzipTextAsync(string outputPath, string text, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using var outputFile = File.Create(outputPath);
        await using var output = new GZipStream(outputFile, CompressionLevel.SmallestSize, leaveOpen: false);
        var bytes = Encoding.UTF8.GetBytes(text);
        await output.WriteAsync(bytes, cancellationToken);
    }

    public async Task CopyAtomicAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidOperationException("Destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        UnixFileMode? destinationMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(fullDestination))
        {
            try { destinationMode = File.GetUnixFileMode(fullDestination); }
            catch { }
        }
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(temp))
                await source.CopyToAsync(destination, cancellationToken);
            if (destinationMode.HasValue && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, destinationMode.Value);
            File.Move(temp, fullDestination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static string ResolveChild(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new InvalidDataException("Snapshot artifact names must be basenames.");
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, fileName));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot artifact escapes its folder.");
        return full;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string NormalizeOwnedDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Owned directory path is required.", parameterName);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetDirectoryName(full) == null || string.IsNullOrWhiteSpace(Path.GetFileName(full)))
            throw new InvalidOperationException($"'{path}' is not a replaceable owned directory.");
        return full;
    }

    private static void EnsureIndependentPaths(string first, string second, string message)
    {
        if (string.Equals(first, second, PathComparison) || IsInside(first, second) || IsInside(second, first))
            throw new InvalidOperationException(message);
        if (string.Equals(Path.GetFileName(first), Path.GetFileName(second), PathComparison))
            throw new InvalidOperationException("Source and SQL roots must have different directory names in the core archive.");
    }

    private static void EnsureFileOutsideOwnedTrees(string file, string source, string sql, string label)
    {
        if (IsInside(file, source) || IsInside(file, sql))
            throw new InvalidOperationException($"The exact-path {label} target cannot live inside an owned source/SQL tree.");
    }

    private static bool IsInside(string candidate, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(root, PathComparison);
    }

    private static string SiblingWorkPath(string target, string label, string operationId)
    {
        var full = Path.GetFullPath(target);
        var parent = Path.GetDirectoryName(full)
            ?? throw new InvalidOperationException($"'{target}' has no parent directory.");
        Directory.CreateDirectory(parent);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"'{target}' is not a replaceable path.");
        return Path.Combine(parent, $".{name}.worldstate-{label}-{operationId}");
    }

    private async Task<CoreArchiveExtraction> ExtractCoreArchiveAsync(
        string archivePath,
        string sourceStage,
        string sqlStage,
        string sourceArchiveRoot,
        string sqlArchiveRoot,
        bool captureLegacyConfig,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(sourceStage);
        Directory.CreateDirectory(sqlStage);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenPaths = new HashSet<string>(comparer);
        var directories = new List<(string Path, UnixFileMode Mode, DateTimeOffset Modified)>();
        var sourceSeen = false;
        var sqlSeen = false;
        var sourceFiles = 0;
        var sqlFiles = 0;
        byte[]? legacyConfig = null;

        await using var file = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeArchiveEntry(entry);
            var normalized = NormalizeArchivePath(entry.Name).TrimEnd('/');
            if (!seenPaths.Add(normalized))
                throw new InvalidDataException($"Core archive contains duplicate entry '{entry.Name}'.");

            if (string.Equals(normalized, "mangosd.conf", StringComparison.OrdinalIgnoreCase))
            {
                if (!captureLegacyConfig || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Unexpected root mangosd.conf in core archive.");
                if (legacyConfig != null)
                    throw new InvalidDataException("Legacy core archive contains more than one root mangosd.conf.");
                legacyConfig = await ReadBoundedAsync(entry.DataStream, 4 * 1024 * 1024, cancellationToken);
                continue;
            }

            var slash = normalized.IndexOf('/');
            var archiveRoot = slash < 0 ? normalized : normalized[..slash];
            var relative = slash < 0 ? "" : normalized[(slash + 1)..];
            string stage;
            bool isSource;
            if (string.Equals(archiveRoot, sourceArchiveRoot, StringComparison.Ordinal))
            {
                sourceSeen = true;
                stage = sourceStage;
                isSource = true;
            }
            else if (string.Equals(archiveRoot, sqlArchiveRoot, StringComparison.Ordinal))
            {
                sqlSeen = true;
                stage = sqlStage;
                isSource = false;
            }
            else
            {
                throw new InvalidDataException($"Unexpected top-level core archive entry '{entry.Name}'.");
            }

            if (relative.Length == 0)
            {
                if (entry.EntryType != TarEntryType.Directory)
                    throw new InvalidDataException($"Core archive root '{entry.Name}' is not a directory.");
                directories.Add((stage, entry.Mode, entry.ModificationTime));
                continue;
            }

            var destination = ResolveArchiveOutput(stage, relative);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(destination);
                directories.Add((destination, entry.Mode, entry.ModificationTime));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (entry.DataStream != null)
                    await entry.DataStream.CopyToAsync(output, cancellationToken);
            }
            ApplyFileMetadata(destination, entry.Mode, entry.ModificationTime);
            if (isSource) sourceFiles++; else sqlFiles++;
        }

        if (!sourceSeen || !sqlSeen)
            throw new InvalidDataException(
                $"Core archive must contain both '{sourceArchiveRoot}/' and '{sqlArchiveRoot}/' roots.");
        if (captureLegacyConfig && legacyConfig == null)
            throw new InvalidDataException("Legacy core archive does not contain one root mangosd.conf.");

        foreach (var directory in directories.OrderByDescending(x => x.Path.Length))
            ApplyFileMetadata(directory.Path, directory.Mode, directory.Modified);

        return new CoreArchiveExtraction
        {
            LegacyConfig = legacyConfig,
            SourceFiles = sourceFiles,
            SqlFiles = sqlFiles
        };
    }

    private static string ResolveArchiveOutput(string root, string relative)
    {
        var normalizedRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        if (!full.StartsWith(fullRoot, PathComparison))
            throw new InvalidDataException($"Archive entry '{relative}' escapes its owned directory.");
        return full;
    }

    private static string NormalizeArchivePath(string name)
    {
        var normalized = name.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream? input, int maximumBytes, CancellationToken cancellationToken)
    {
        if (input == null) return Array.Empty<byte>();
        await using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException($"Archive file exceeds the {maximumBytes:N0}-byte safety limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }

    private static async Task CopyNewFileAsync(
        string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static void PreserveExistingUnixMode(string existingPath, string stagedPath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(existingPath)) return;
        File.SetUnixFileMode(stagedPath, File.GetUnixFileMode(existingPath));
    }

    private static void ApplyFileMetadata(string path, UnixFileMode mode, DateTimeOffset modified)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode);
        if (Directory.Exists(path)) Directory.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        else File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void EnsureSafeArchivePath(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            throw new InvalidDataException($"Unsafe tar entry '{name}'.");
    }

    private static void EnsureSafeArchiveEntry(TarEntry entry)
    {
        EnsureSafeArchivePath(entry.Name);
        if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            throw new InvalidDataException(
                $"Tar entry '{entry.Name}' has unsupported type '{entry.EntryType}'. " +
                "World-state core archives may contain only regular files and directories.");
    }

    private sealed class CoreArchiveExtraction
    {
        public byte[]? LegacyConfig { get; init; }
        public int SourceFiles { get; init; }
        public int SqlFiles { get; init; }
    }

    private sealed class PathReplacementTransaction
    {
        private readonly string _operationId;
        private readonly List<Replacement> _replacements = new();

        public PathReplacementTransaction(string operationId) => _operationId = operationId;

        public void ReplaceDirectory(string staged, string target)
        {
            if (!Directory.Exists(staged))
                throw new DirectoryNotFoundException($"Staged directory '{staged}' is missing.");
            if (File.Exists(target))
                throw new InvalidOperationException($"Owned directory target '{target}' is a file.");

            var backup = SiblingWorkPath(target, "backup", _operationId);
            var hadOriginal = Directory.Exists(target);
            if (hadOriginal) Directory.Move(target, backup);
            try
            {
                Directory.Move(staged, target);
                _replacements.Add(new Replacement(target, backup, hadOriginal, IsDirectory: true));
            }
            catch
            {
                if (hadOriginal && Directory.Exists(backup) && !Directory.Exists(target))
                    Directory.Move(backup, target);
                throw;
            }
        }

        public void ReplaceFile(string staged, string target)
        {
            if (!File.Exists(staged))
                throw new FileNotFoundException("Staged file is missing.", staged);
            if (Directory.Exists(target))
                throw new InvalidOperationException($"Owned file target '{target}' is a directory.");

            var backup = SiblingWorkPath(target, "backup", _operationId);
            var hadOriginal = File.Exists(target);
            if (hadOriginal) File.Move(target, backup);
            try
            {
                File.Move(staged, target);
                _replacements.Add(new Replacement(target, backup, hadOriginal, IsDirectory: false));
            }
            catch
            {
                if (hadOriginal && File.Exists(backup) && !File.Exists(target))
                    File.Move(backup, target);
                throw;
            }
        }

        public void Commit()
        {
            foreach (var replacement in _replacements)
            {
                try
                {
                    if (!replacement.HadOriginal) continue;
                    if (replacement.IsDirectory) DeleteDirectoryIfPresent(replacement.Backup);
                    else DeleteFileIfPresent(replacement.Backup);
                }
                catch
                {
                    // The new core is already complete. A leftover, uniquely named backup is
                    // recoverable and safer than turning cleanup into a destructive rollback.
                }
            }
            _replacements.Clear();
        }

        public void Rollback()
        {
            var errors = new List<Exception>();
            for (var i = _replacements.Count - 1; i >= 0; i--)
            {
                try
                {
                    Restore(_replacements[i], i);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
            _replacements.Clear();
            if (errors.Count > 0)
                throw new AggregateException("One or more owned core paths could not be rolled back.", errors);
        }

        private void Restore(Replacement replacement, int index)
        {
            var failed = SiblingWorkPath(replacement.Target, $"failed-{index}", _operationId);
            if (replacement.IsDirectory)
            {
                if (replacement.HadOriginal && !Directory.Exists(replacement.Backup))
                    throw new DirectoryNotFoundException($"Rollback backup '{replacement.Backup}' is missing.");
                if (Directory.Exists(replacement.Target)) Directory.Move(replacement.Target, failed);
                if (replacement.HadOriginal) Directory.Move(replacement.Backup, replacement.Target);
                try { DeleteDirectoryIfPresent(failed); } catch { }
            }
            else
            {
                if (replacement.HadOriginal && !File.Exists(replacement.Backup))
                    throw new FileNotFoundException("Rollback backup is missing.", replacement.Backup);
                if (File.Exists(replacement.Target)) File.Move(replacement.Target, failed);
                if (replacement.HadOriginal) File.Move(replacement.Backup, replacement.Target);
                try { DeleteFileIfPresent(failed); } catch { }
            }
        }

        private sealed record Replacement(string Target, string Backup, bool HadOriginal, bool IsDirectory);
    }
}

public sealed class CoreArtifactRestoreResult
{
    public bool LegacyConfig { get; init; }
    public int SourceFiles { get; init; }
    public int SqlFiles { get; init; }
}

public sealed class DatabaseDumpDefinition
{
    public string DatabaseName { get; init; } = "";
    public string CreateStatement { get; init; } = "";
    public string UseStatement { get; init; } = "";
    public string? CharacterSet { get; init; }
    public string? Collation { get; init; }
}
