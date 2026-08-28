using MangosSuperUI.BotLogic.Tracking;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class CircuitTraceSourceReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "circuit-source-reader-" + Guid.NewGuid().ToString("N"));

    public CircuitTraceSourceReaderTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public void CSharpBuildPath_IsRemappedAndReturnsNumberedContextWithTarget()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "deployed-app")).FullName;
        string relative = Path.Combine("BotLogic", "Brain", "Decision.cs");
        string deployedFile = Path.Combine(contentRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(deployedFile)!);
        File.WriteAllLines(deployedFile,
        [
            "namespace Example;",
            "public static class Decision",
            "{",
            "    public static bool Choose(int health)",
            "    {",
            "        if (health < 30)",
            "            return false;",
            "        return true;",
            "    }",
            "}"
        ]);

        var site = new CircuitTrace.ProbeSite(
            7,
            Path.Combine("Z:\\stale-build", "MangosSuperUI", relative),
            6,
            "health decision");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(
            site, contentRoot, before: 2, after: 1);

        Assert.True(result.Available, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("BotLogic/Brain/Decision.cs", result.DisplayFile);
        Assert.Equal("csharp", result.Language);
        Assert.Equal(6, result.TargetLine);
        Assert.Equal(4, result.StartLine);
        Assert.Equal(7, result.EndLine);
        Assert.Equal([4, 5, 6, 7], result.Lines.Select(line => line.Number));
        CircuitTraceSourceLine target = Assert.Single(result.Lines, line => line.IsTarget);
        Assert.Equal(6, target.Number);
        Assert.Equal("        if (health < 30)", target.Text);
    }

    [Fact]
    public void ExternalCSharpRoot_IsUsedWhenPublishedContentRootHasNoSource()
    {
        string publishedRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "published-no-source")).FullName;
        string sourceRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "external-source")).FullName;
        string relative = Path.Combine("BotLogic", "Brain", "ExternalDecision.cs");
        string sourceFile = Path.Combine(sourceRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllLines(sourceFile,
        [
            "namespace Example;",
            "public static class ExternalDecision",
            "{",
            "    public static bool Choose(bool ready)",
            "    {",
            "        return ready;",
            "    }",
            "}"
        ]);
        var site = new CircuitTrace.ProbeSite(
            11,
            Path.Combine("Z:\\build-host", "MangosSuperUI", relative),
            6,
            "external source decision");

        CircuitTraceSourceSnippet unavailable = CircuitTraceSourceReader.Read(site, publishedRoot);
        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(
            site,
            sourceRoot,
            before: 2,
            after: 1);

        Assert.False(unavailable.Available);
        Assert.True(result.Available, result.Error);
        Assert.Equal("BotLogic/Brain/ExternalDecision.cs", result.DisplayFile);
        Assert.Equal(4, result.StartLine);
        Assert.Equal(7, result.EndLine);
        Assert.Equal("        return ready;", Assert.Single(result.Lines, line => line.IsTarget).Text);
    }

    [Fact]
    public void ContextArguments_AreClampedToReaderLimit()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "clamp-app")).FullName;
        string file = Path.Combine(contentRoot, "Clamp.cs");
        File.WriteAllLines(file, Enumerable.Range(1, 60).Select(number => $"line {number}"));
        var site = new CircuitTrace.ProbeSite(8, file, 30, "clamp");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(
            site, contentRoot, before: int.MaxValue, after: -50);

        Assert.True(result.Available, result.Error);
        Assert.Equal(30 - CircuitTraceSourceReader.MaxContextLines, result.StartLine);
        Assert.Equal(30, result.EndLine);
        Assert.Equal(CircuitTraceSourceReader.MaxContextLines + 1, result.Lines.Count);
    }

    [Fact]
    public void CppPath_UsesUniqueIndexedSuffixUnderConfiguredRoot()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "app")).FullName;
        string cppRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "core")).FullName;
        string indexed = "src/game/SuperUiContent/SuiBots/Combat/BotDecision.cpp";
        string sourceFile = Path.Combine(cppRoot, indexed.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllLines(sourceFile, ["void choose()", "{", "    Attack();", "}"]);
        var site = new CircuitTrace.ProbeSite(100001, "cpp/Combat/BotDecision.cpp", 3, "attack");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(
            site,
            contentRoot,
            cppRoot,
            ["src/game/Other.cpp", indexed],
            before: 1,
            after: 0);

        Assert.True(result.Available, result.Error);
        Assert.Equal(indexed, result.DisplayFile);
        Assert.Equal("cpp", result.Language);
        Assert.Equal(2, result.StartLine);
        Assert.Equal(3, result.EndLine);
        Assert.Equal("    Attack();", Assert.Single(result.Lines, line => line.IsTarget).Text);
    }

    [Fact]
    public void BareCppName_WithMultipleIndexedMatchesIsRejectedAsAmbiguous()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "ambiguous-app")).FullName;
        string cppRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "ambiguous-core")).FullName;
        string first = "src/game/SuperUiContent/SuiBots/First/Decision.cpp";
        string second = "src/game/SuperUiContent/SuiBots/Second/Decision.cpp";
        WriteCpp(cppRoot, first);
        WriteCpp(cppRoot, second);
        var site = new CircuitTrace.ProbeSite(100002, "cpp/Decision.cpp", 1, "ambiguous");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(
            site, contentRoot, cppRoot, [first, second]);

        Assert.False(result.Available);
        Assert.Contains("ambiguous", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void MissingSource_ReturnsUnavailableWithoutThrowing()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "missing-app")).FullName;
        var site = new CircuitTrace.ProbeSite(
            9,
            Path.Combine("C:\\old-checkout", "MangosSuperUI", "BotLogic", "Missing.cs"),
            42,
            "missing");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(site, contentRoot);

        Assert.False(result.Available);
        Assert.Equal("BotLogic/Missing.cs", result.DisplayFile);
        Assert.Contains("unavailable", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(42, result.TargetLine);
        Assert.Equal(0, result.StartLine);
        Assert.Equal(0, result.EndLine);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void TraversalMetadata_CannotReadOutsideAllowedRoot()
    {
        string contentRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "safe-app")).FullName;
        string secret = Path.Combine(_tempRoot, "Secret.cs");
        File.WriteAllText(secret, "secret");
        var site = new CircuitTrace.ProbeSite(
            10,
            Path.Combine("C:\\old", "MangosSuperUI", "..", "Secret.cs"),
            1,
            "malformed");

        CircuitTraceSourceSnippet result = CircuitTraceSourceReader.Read(site, contentRoot);

        Assert.False(result.Available);
        Assert.Empty(result.Lines);
        Assert.DoesNotContain(_tempRoot, result.DisplayFile, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteCpp(string root, string relative)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "probe();");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }
}
