using System.IO.Compression;
using System.Text;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class CircuitTraceSourceServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "circuit-source-service-" + Guid.NewGuid().ToString("N"));

    public CircuitTraceSourceServiceTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public void ConfiguredExternalCSharpAndCppRoots_ReportReady()
    {
        string contentRoot = EmptyDirectory("published-app");
        string csharpRoot = CreateCSharpRoot("external-superui");
        string cppRoot = CreateCppRoot("external-vmangos-src");
        CircuitTraceSourceService service = CreateService(
            contentRoot,
            csharpRoot,
            cppRoot);

        CircuitTraceSourceSetupStatus status = service.GetStatus();

        Assert.True(status.Ready);
        Assert.True(status.CSharp.Ready);
        Assert.Equal("csharp", status.CSharp.Kind);
        Assert.Equal("configured folder", status.CSharp.Origin);
        Assert.Equal(Path.GetFullPath(csharpRoot), status.CSharp.Root);
        Assert.True(status.Cpp.Ready);
        Assert.Equal("cpp", status.Cpp.Kind);
        Assert.Equal("configured folder", status.Cpp.Origin);
        Assert.Equal(Path.GetFullPath(cppRoot), status.Cpp.Root);
        Assert.Equal(Path.GetFullPath(csharpRoot), service.GetCSharpRoot());
        Assert.Equal(Path.GetFullPath(cppRoot), service.GetCppRoot());
        Assert.Matches("^[0-9A-F]{16}$", status.SourceVersion);
    }

    [Fact]
    public void MissingStatus_IdentifiesTwoDistinctRequiredSourcePackages()
    {
        string contentRoot = EmptyDirectory("published-missing-sources");
        CircuitTraceSourceService service = CreateService(
            contentRoot,
            csharpRoot: null,
            cppRoot: null);

        CircuitTraceSourceSetupStatus status = service.GetStatus();

        Assert.False(status.Ready);
        Assert.False(status.CSharp.Ready);
        Assert.Equal("csharp", status.CSharp.Kind);
        Assert.Equal("MangosSuperUI C# source", status.CSharp.Label);
        Assert.Contains("MangosSuperUI project folder", status.CSharp.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(status.Cpp.Ready);
        Assert.Equal("cpp", status.Cpp.Kind);
        Assert.Equal("SuperUI-Core C++ source (VMaNGOS fork)", status.Cpp.Label);
        Assert.Contains("SuperUI-Core src folder", status.Cpp.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(status.CSharp.Label, status.Cpp.Label);
    }

    [Fact]
    public async Task CSharpZipUpload_InstallsPackageAndCompletesSetup()
    {
        string contentRoot = EmptyDirectory("published-upload-app");
        string cppRoot = CreateCppRoot("configured-cpp-for-upload");
        CircuitTraceSourceService service = CreateService(
            contentRoot,
            csharpRoot: null,
            cppRoot);
        using MemoryStream archive = BuildZip(
            ("release/MangosSuperUI/MangosSuperUI.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />"),
            ("release/MangosSuperUI/BotLogic/Brain/UploadedDecision.cs", "namespace Uploaded; public sealed class UploadedDecision { }"));
        long archiveLength = archive.Length;

        CircuitTraceSourceUploadResult result = await service.UploadArchiveAsync(
            CircuitTraceSourceKind.CSharp,
            archive,
            "superui-source.zip",
            archiveLength);

        Assert.Equal(CircuitTraceSourceKind.CSharp, result.Kind);
        Assert.Equal(1, result.SourceFileCount);
        Assert.Equal(archiveLength, result.ArchiveBytes);
        Assert.True(result.Source.Ready);
        Assert.Equal("uploaded package", result.Source.Origin);
        Assert.True(result.Status.Ready);
        Assert.True(File.Exists(Path.Combine(
            result.Source.Root!,
            "BotLogic",
            "Brain",
            "UploadedDecision.cs")));
        Assert.Equal(result.Status.SourceVersion, service.GetStatus().SourceVersion);
    }

    [Fact]
    public async Task TraversalZip_IsRejectedWithoutReplacingInstalledPackage()
    {
        string contentRoot = EmptyDirectory("published-atomic-app");
        string cppRoot = CreateCppRoot("configured-cpp-for-atomic-test");
        CircuitTraceSourceService service = CreateService(
            contentRoot,
            csharpRoot: null,
            cppRoot);
        using (MemoryStream valid = BuildZip(
                   ("MangosSuperUI/MangosSuperUI.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />"),
                   ("MangosSuperUI/BotLogic/Brain/KeepMe.cs", "// installed package")))
        {
            await service.UploadArchiveAsync(
                CircuitTraceSourceKind.CSharp,
                valid,
                "valid.zip",
                valid.Length);
        }

        CircuitTraceSourceSetupStatus before = service.GetStatus();
        string installedRoot = Assert.IsType<string>(before.CSharp.Root);
        string installedFile = Path.Combine(installedRoot, "BotLogic", "Brain", "KeepMe.cs");
        Assert.True(File.Exists(installedFile));

        using MemoryStream unsafeArchive = BuildZip(
            ("MangosSuperUI/MangosSuperUI.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />"),
            ("MangosSuperUI/BotLogic/Brain/Replacement.cs", "// must not replace"),
            ("../escape.cs", "// must never be written"));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UploadArchiveAsync(
                CircuitTraceSourceKind.CSharp,
                unsafeArchive,
                "unsafe.zip",
                unsafeArchive.Length));

        Assert.Contains("unsafe path", error.Message, StringComparison.OrdinalIgnoreCase);
        CircuitTraceSourceSetupStatus after = service.GetStatus();
        Assert.True(after.Ready);
        Assert.Equal(before.SourceVersion, after.SourceVersion);
        Assert.Equal(installedRoot, after.CSharp.Root);
        Assert.Equal("// installed package", File.ReadAllText(installedFile));
        Assert.False(File.Exists(Path.Combine(_tempRoot, "packages", "escape.cs")));
        Assert.False(File.Exists(Path.Combine(installedRoot, "BotLogic", "Brain", "Replacement.cs")));
    }

    [Fact]
    public async Task UploadedStorageInsidePublishedTree_IsRejected()
    {
        string contentRoot = EmptyDirectory("published-unsafe-storage-app");
        string cppRoot = CreateCppRoot("configured-cpp-for-storage-test");
        string unsafeStorage = Path.Combine(contentRoot, "wwwroot", "source-packages");
        CircuitTraceSourceService service = CreateService(
            contentRoot,
            csharpRoot: null,
            cppRoot,
            unsafeStorage);

        CircuitTraceSourceSetupStatus status = service.GetStatus();

        Assert.False(status.Ready);
        Assert.False(status.CSharp.Ready);
        Assert.Contains("outside the published application", status.CSharp.Message,
            StringComparison.OrdinalIgnoreCase);

        using MemoryStream archive = BuildZip(
            ("MangosSuperUI/MangosSuperUI.csproj", "<Project />"),
            ("MangosSuperUI/BotLogic/Decision.cs", "// source"));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadArchiveAsync(
                CircuitTraceSourceKind.CSharp,
                archive,
                "source.zip",
                archive.Length));
        Assert.Contains("outside the published application", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(unsafeStorage));
    }

    private CircuitTraceSourceService CreateService(
        string contentRoot,
        string? csharpRoot,
        string? cppRoot,
        string? packageRoot = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["CircuitTrace:SourcePackageDirectory"] = packageRoot ?? Path.Combine(_tempRoot, "packages"),
            ["CircuitTrace:CSharpSourcePath"] = csharpRoot,
            ["Vmangos:VmangosSourcePath"] = cppRoot
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot")
        };
        return new CircuitTraceSourceService(
            environment,
            configuration,
            NullLogger<CircuitTraceSourceService>.Instance);
    }

    private string EmptyDirectory(string name) =>
        Directory.CreateDirectory(Path.Combine(_tempRoot, name)).FullName;

    private string CreateCSharpRoot(string name)
    {
        string root = EmptyDirectory(name);
        Directory.CreateDirectory(Path.Combine(root, "BotLogic", "Brain"));
        File.WriteAllText(Path.Combine(root, "MangosSuperUI.csproj"), "<Project />");
        File.WriteAllText(
            Path.Combine(root, "BotLogic", "Brain", "Decision.cs"),
            "namespace External; public sealed class Decision { }");
        return root;
    }

    private string CreateCppRoot(string name)
    {
        string root = EmptyDirectory(name);
        string suiBots = Path.Combine(root, "game", "SuperUiContent", "SuiBots");
        Directory.CreateDirectory(suiBots);
        File.WriteAllText(Path.Combine(suiBots, "CircuitDecision.cpp"), "void Decide() {}\n");
        return root;
    }

    private static MemoryStream BuildZip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using Stream entryStream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes);
            }
        }

        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MangosSuperUI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
