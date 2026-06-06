using System.Diagnostics;

using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;

using FluentAssertions;

using NuGet.Versioning;

namespace FieldCure.ToolHost.Tests;

public sealed class DnxLiteRunnerTests
{
    [Xunit.Fact]
    public async Task StartAsync_ForwardsEnvironmentOptionsToLauncher()
    {
        var launcher = new CapturingLauncher();
        var runner = new DnxLiteRunner(
            new DotnetEnvironment
            {
                InstalledSdks = [],
                InstalledRuntimes = [],
                DotnetMuxerPath = "dotnet",
                NuGetGlobalPackagesFolder = "packages",
                RuntimeIdentifier = "test-x64",
            },
            new StubResolver(),
            new StubExtractor(),
            launcher);

        var additionalEnvironment = new Dictionary<string, string?>
        {
            ["ONE"] = "1",
            ["TWO"] = null,
        };

        using var process = await runner.StartAsync(new ToolInvocationRequest
        {
            PackageId = "Example.Tool",
            ToolArguments = ["hello"],
            InheritEnvironmentVariables = false,
            AdditionalEnvironment = additionalEnvironment,
        });

        _ = process.Should().NotBeNull();
        _ = launcher.Request.Should().NotBeNull();
        _ = launcher.Request!.InheritEnvironmentVariables.Should().BeFalse();
        _ = launcher.Request.AdditionalEnvironment.Should().BeSameAs(additionalEnvironment);
    }

    private sealed class StubResolver : IPackageResolver
    {
        public Task<PackageResolution> ResolveAsync(PackageResolutionRequest request, CancellationToken ct) =>
            Task.FromResult(new PackageResolution(
                request.PackageId,
                NuGetVersion.Parse("1.0.0"),
                "test",
                WasCacheHit: true));
    }

    private sealed class StubExtractor : IToolExtractor
    {
        public Task<ExtractedToolLayout> EnsureExtractedAsync(PackageResolution resolution, CancellationToken ct) =>
            Task.FromResult(new ExtractedToolLayout(
                PackagePath: "package",
                ToolsFolder: AppContext.BaseDirectory,
                TargetFramework: "net8.0",
                RuntimeIdentifier: "any",
                ToolSettings: new DotnetToolSettings
                {
                    Commands =
                    [
                        new DotnetToolCommand
                        {
                            Name = "example",
                            EntryPoint = "example.dll",
                            Runner = "dotnet",
                        },
                    ],
                }));
    }

    private sealed class CapturingLauncher : IToolLauncher
    {
        public LaunchRequest? Request { get; private set; }

        public Process Start(LaunchRequest request)
        {
            Request = request;
            return new Process();
        }
    }
}
