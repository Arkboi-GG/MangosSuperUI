namespace MangosSuperUI.BotLogic.Tracking;

/// <summary>
/// Reads the small, line-numbered source window around a registered circuit probe.
/// Paths in a trace are identities, not read authority: C# is confined to the
/// configured application source root and C++ is confined to its own configured root.
/// </summary>
internal static class CircuitTraceSourceReader
{
    internal const int MaxContextLines = 20;

    internal static CircuitTraceSourceSnippet Read(
        CircuitTrace.ProbeSite site,
        string? csharpSourceRoot,
        string? cppSourceRoot = null,
        IEnumerable<string>? indexedRelativePaths = null,
        int before = 4,
        int after = 2)
    {
        ArgumentNullException.ThrowIfNull(site);

        int clampedBefore = Math.Clamp(before, 0, MaxContextLines);
        int clampedAfter = Math.Clamp(after, 0, MaxContextLines);
        bool isCpp = site.File.StartsWith("cpp/", StringComparison.OrdinalIgnoreCase)
            || site.File.StartsWith("cpp\\", StringComparison.OrdinalIgnoreCase);
        string language = isCpp ? "cpp" : "csharp";

        SourceResolution resolution;
        try
        {
            resolution = isCpp
                ? ResolveCpp(site.File, cppSourceRoot, indexedRelativePaths)
                : ResolveCSharp(site.File, csharpSourceRoot);
        }
        catch (Exception ex) when (IsPathOrIoException(ex))
        {
            return Unavailable(site, language, SafeDisplayFile(site.File, isCpp),
                "Source file is unavailable.");
        }

        if (!resolution.Available)
            return Unavailable(site, language, resolution.DisplayFile, resolution.Error!);

        try
        {
            string[] sourceLines = File.ReadAllLines(resolution.AbsolutePath!);
            if (site.Line < 1 || site.Line > sourceLines.Length)
            {
                return Unavailable(site, language, resolution.DisplayFile,
                    "Target line is outside the source file.");
            }

            int start = Math.Max(1, site.Line - clampedBefore);
            int end = Math.Min(sourceLines.Length, site.Line + clampedAfter);
            var lines = new List<CircuitTraceSourceLine>(end - start + 1);
            for (int line = start; line <= end; line++)
                lines.Add(new CircuitTraceSourceLine(line, sourceLines[line - 1], line == site.Line));

            return new CircuitTraceSourceSnippet(
                Available: true,
                Error: null,
                DisplayFile: resolution.DisplayFile,
                TargetLine: site.Line,
                StartLine: start,
                EndLine: end,
                Language: language,
                Lines: lines);
        }
        catch (Exception ex) when (IsPathOrIoException(ex))
        {
            return Unavailable(site, language, resolution.DisplayFile,
                "Source file is unavailable.");
        }
    }

    private static SourceResolution ResolveCSharp(string callerFilePath, string? csharpSourceRoot)
    {
        if (!TryGetRoot(csharpSourceRoot, out string root))
            return SourceResolution.Failed("<unavailable>", "C# source root is not configured.");

        // CallerFilePath is normally absolute. It is usable directly only when it
        // is already within this deployment's configured source root.
        // Use the host's rooted-path rules here. A Windows CallerFilePath on a
        // Linux deployment (or vice versa) is not directly readable; it must go
        // through the project-suffix remap below.
        if (Path.IsPathRooted(callerFilePath))
        {
            string absolute = Path.GetFullPath(callerFilePath);
            if (IsWithinRoot(root, absolute))
            {
                string display = RelativeDisplay(root, absolute);
                if (!HasCSharpExtension(absolute))
                    return SourceResolution.Failed(display, "Source file type is unavailable.");
                return File.Exists(absolute)
                    ? SourceResolution.Found(absolute, display)
                    : SourceResolution.Failed(display, "Source file is unavailable.");
            }
        }

        // Build hosts stamp their own absolute checkout into CallerFilePath. On a
        // different host, retain only the suffix below the MangosSuperUI project
        // directory and remap that suffix beneath this configured source root.
        if (!TryProjectSuffix(callerFilePath, out string suffix)
            || !HasCSharpExtension(suffix)
            || !TryCombineUnderRoot(root, suffix, out string remapped))
        {
            return SourceResolution.Failed(
                SafeDisplayFile(callerFilePath, isCpp: false),
                "Source path is outside the allowed source root.");
        }

        string remappedDisplay = RelativeDisplay(root, remapped);
        return File.Exists(remapped)
            ? SourceResolution.Found(remapped, remappedDisplay)
            : SourceResolution.Failed(remappedDisplay, "Source file is unavailable.");
    }

    private static SourceResolution ResolveCpp(
        string siteFile,
        string? cppSourceRoot,
        IEnumerable<string>? indexedRelativePaths)
    {
        string raw = siteFile.Length > 4 ? siteFile[4..] : string.Empty;
        if (!TryNormalizeRelative(raw, out string requested) || !HasCppExtension(requested))
            return SourceResolution.Failed("<unavailable>", "C++ source path is unavailable.");

        if (!TryGetRoot(cppSourceRoot, out string root))
            return SourceResolution.Failed(requested, "C++ source root is not configured.");

        // Combine direct, well-known SuiBots layout, and source-index matches.
        // A HashSet makes an indexed entry and its direct equivalent one result.
        var matches = new Dictionary<string, string>(PathComparer);
        AddExistingCandidate(matches, root, requested);

        string lower = requested.ToLowerInvariant();
        if (lower.StartsWith("game/superuicontent/suibots/", StringComparison.Ordinal))
        {
            AddExistingCandidate(matches, root, "src/" + requested);
        }
        else if (lower.StartsWith("superuicontent/suibots/", StringComparison.Ordinal))
        {
            AddExistingCandidate(matches, root, "game/" + requested);
            AddExistingCandidate(matches, root, "src/game/" + requested);
        }
        else if (lower.StartsWith("suibots/", StringComparison.Ordinal))
        {
            AddExistingCandidate(matches, root, "game/SuperUiContent/" + requested);
            AddExistingCandidate(matches, root, "src/game/SuperUiContent/" + requested);
        }
        else if (!requested.Contains('/'))
        {
            AddExistingCandidate(matches, root, "game/SuperUiContent/SuiBots/" + requested);
            AddExistingCandidate(matches, root, "src/game/SuperUiContent/SuiBots/" + requested);
        }

        if (indexedRelativePaths != null)
        {
            foreach (string indexedPath in indexedRelativePaths)
            {
                if (!TryNormalizeRelative(indexedPath, out string indexed)
                    || !HasCppExtension(indexed)
                    || !IsSameOrSuffix(indexed, requested))
                    continue;

                AddExistingCandidate(matches, root, indexed);
            }
        }

        if (matches.Count > 1)
            return SourceResolution.Failed(requested,
                "C++ source file name is ambiguous in the source index.");

        if (matches.Count == 0)
            return SourceResolution.Failed(requested, "Source file is unavailable.");

        KeyValuePair<string, string> match = matches.Single();
        return SourceResolution.Found(match.Key, match.Value);
    }

    private static void AddExistingCandidate(
        Dictionary<string, string> matches,
        string root,
        string relativePath)
    {
        if (!TryCombineUnderRoot(root, relativePath, out string absolute) || !File.Exists(absolute))
            return;

        matches.TryAdd(absolute, RelativeDisplay(root, absolute));
    }

    private static bool IsSameOrSuffix(string indexed, string requested)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return indexed.Equals(requested, comparison)
            || indexed.EndsWith("/" + requested, comparison);
    }

    private static bool TryGetRoot(string? configuredRoot, out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredRoot)) return false;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
        return Directory.Exists(root);
    }

    private static bool TryProjectSuffix(string path, out string suffix)
    {
        suffix = string.Empty;
        string normalized = path.Replace('\\', '/');
        string[] pieces = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int project = -1;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Equals("MangosSuperUI", StringComparison.OrdinalIgnoreCase))
                project = i;
        }

        if (project < 0 || project == pieces.Length - 1) return false;
        return TryNormalizeRelative(string.Join('/', pieces[(project + 1)..]), out suffix);
    }

    private static bool TryNormalizeRelative(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || LooksRooted(path) || path.IndexOf('\0') >= 0)
            return false;

        string[] pieces = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0) return false;
        foreach (string piece in pieces)
        {
            if (piece is "." or ".." || piece.Contains(':')) return false;
        }

        normalized = string.Join('/', pieces);
        return true;
    }

    private static bool TryCombineUnderRoot(string root, string relativePath, out string absolute)
    {
        absolute = string.Empty;
        if (!TryNormalizeRelative(relativePath, out string normalized)) return false;
        absolute = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return IsWithinRoot(root, absolute);
    }

    private static bool IsWithinRoot(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool LooksRooted(string path)
    {
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\')) return true;
        return path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
    }

    private static bool HasCSharpExtension(string path) =>
        Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool HasCppExtension(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hh" or ".hpp" or ".hxx" or ".inl";

    private static string RelativeDisplay(string root, string absolute) =>
        Path.GetRelativePath(root, absolute).Replace('\\', '/');

    private static string SafeDisplayFile(string file, bool isCpp)
    {
        string candidate = isCpp && file.Length > 4 ? file[4..] : file;
        if (isCpp && TryNormalizeRelative(candidate, out string cppRelative)) return cppRelative;
        if (!isCpp && TryProjectSuffix(candidate, out string csRelative)) return csRelative;

        string name = Path.GetFileName(candidate.Replace('\\', Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? "<unavailable>" : name;
    }

    private static bool IsPathOrIoException(Exception ex) => ex is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
        or System.Security.SecurityException;

    private static CircuitTraceSourceSnippet Unavailable(
        CircuitTrace.ProbeSite site,
        string language,
        string displayFile,
        string error) =>
        new(
            Available: false,
            Error: error,
            DisplayFile: displayFile,
            TargetLine: site.Line,
            StartLine: 0,
            EndLine: 0,
            Language: language,
            Lines: Array.Empty<CircuitTraceSourceLine>());

    private sealed record SourceResolution(
        bool Available,
        string? AbsolutePath,
        string DisplayFile,
        string? Error)
    {
        internal static SourceResolution Found(string absolutePath, string displayFile) =>
            new(true, absolutePath, displayFile, null);

        internal static SourceResolution Failed(string displayFile, string error) =>
            new(false, null, displayFile, error);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal sealed record CircuitTraceSourceSnippet(
    bool Available,
    string? Error,
    string DisplayFile,
    int TargetLine,
    int StartLine,
    int EndLine,
    string Language,
    IReadOnlyList<CircuitTraceSourceLine> Lines);

internal sealed record CircuitTraceSourceLine(int Number, string Text, bool IsTarget);
