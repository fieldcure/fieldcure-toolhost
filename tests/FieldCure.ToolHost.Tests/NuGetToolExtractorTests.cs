using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;

using FluentAssertions;

using NuGet.Versioning;

namespace FieldCure.ToolHost.Tests;

public sealed class NuGetToolExtractorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _globalPackages;

    public NuGetToolExtractorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FieldCure-ToolHost-Extractor-Tests-" + Guid.NewGuid().ToString("N"));
        _globalPackages = Path.Combine(_tempDirectory, "packages");
        _ = Directory.CreateDirectory(_globalPackages);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Xunit.Fact]
    public void DetermineHostFramework_UsesHighestInstalledRuntime()
    {
        var environment = CreateEnvironment("8.0.22", "9.0.5");

        var framework = NuGetToolExtractor.DetermineHostFramework(environment).GetShortFolderName();

        _ = framework.Should().Be("net9.0");
    }

    [Xunit.Fact]
    public async Task EnsureExtractedAsync_DoesNotSelectNet10ToolForNet8OnlyHost()
    {
        var packagePath = Path.Combine(_globalPackages, "demo.tool", "1.0.0");
        _ = Directory.CreateDirectory(packagePath);
        await File.WriteAllTextAsync(Path.Combine(packagePath, ".nupkg.metadata"), string.Empty);
        await CreateToolFolderAsync(packagePath, "net8.0");
        await CreateToolFolderAsync(packagePath, "net10.0");

        var environment = CreateEnvironment("8.0.22");
        NuGetToolExtractor extractor = new(environment);

        var layout = await extractor.EnsureExtractedAsync(
            new PackageResolution("Demo.Tool", NuGetVersion.Parse("1.0.0"), "cache", WasCacheHit: true),
            CancellationToken.None);

        _ = layout.TargetFramework.Should().Be("net8.0");
        _ = layout.ToolSettings.Commands[0].UsesDotnetRunner.Should().BeTrue();
    }

    private static async Task CreateToolFolderAsync(string packagePath, string tfm)
    {
        var toolsFolder = Path.Combine(packagePath, "tools", tfm, "any");
        _ = Directory.CreateDirectory(toolsFolder);
        await File.WriteAllTextAsync(Path.Combine(toolsFolder, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="demo" EntryPoint="demo.dll" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """);
    }

    private DotnetEnvironment CreateEnvironment(params string[] runtimes)
    {
        return new DotnetEnvironment
        {
            InstalledSdks = Array.Empty<string>(),
            InstalledRuntimes = runtimes,
            DotnetMuxerPath = "dotnet",
            NuGetGlobalPackagesFolder = _globalPackages,
            RuntimeIdentifier = "win-x64",
            HasSdk10OrLater = false,
        };
    }
}
