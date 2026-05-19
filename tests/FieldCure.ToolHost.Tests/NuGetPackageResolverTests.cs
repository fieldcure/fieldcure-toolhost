using FieldCure.ToolHost.Resolution;

using FluentAssertions;

namespace FieldCure.ToolHost.Tests;

public sealed class NuGetPackageResolverTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _indexPath;

    public NuGetPackageResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FieldCure-ToolHost-Resolver-Tests-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_tempDirectory);
        _indexPath = Path.Combine(_tempDirectory, "_index.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Xunit.Fact]
    public async Task CachedOnly_DoesNotReturnPinnedVersionOutsideRequestedConstraint()
    {
        using ToolCacheIndexStore store = new(_indexPath);
        await SavePinnedAsync(store, "3.5.0", versionConstraint: "3.*", allowPrerelease: false);
        NuGetPackageResolver resolver = new(new NuGetPackageResolverOptions(), store);

        Func<Task> act = () => resolver.ResolveAsync(new PackageResolutionRequest(
            PackageId: "demo.tool",
            Policy: ToolVersionPolicy.CachedOnly,
            VersionConstraint: "2.*",
            AllowPrerelease: false,
            ExplicitVersion: null), CancellationToken.None);

        _ = await act.Should().ThrowAsync<PackageNotFoundException>()
            .WithMessage("Cached pinned version '3.5.0' does not satisfy VersionConstraint '2.*'.");
    }

    [Xunit.Fact]
    public async Task CachedWithRefresh_FallsThroughWhenPinnedVersionDoesNotSatisfyConstraint()
    {
        var configPath = await WriteEmptyNuGetConfigAsync();
        using ToolCacheIndexStore store = new(_indexPath);
        await SavePinnedAsync(store, "3.5.0", versionConstraint: "3.*", allowPrerelease: false);
        NuGetPackageResolver resolver = new(new NuGetPackageResolverOptions
        {
            NuGetConfigFile = configPath,
        }, store);

        Func<Task> act = () => resolver.ResolveAsync(new PackageResolutionRequest(
            PackageId: "demo.tool",
            Policy: ToolVersionPolicy.CachedWithRefresh,
            VersionConstraint: "2.*",
            AllowPrerelease: false,
            ExplicitVersion: null), CancellationToken.None);

        _ = await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No NuGet sources are configured*");
    }

    [Xunit.Fact]
    public async Task CachedOnly_DoesNotReturnPrereleaseWhenPrereleaseIsNotAllowed()
    {
        using ToolCacheIndexStore store = new(_indexPath);
        await SavePinnedAsync(store, "2.0.0-beta.1", versionConstraint: null, allowPrerelease: true);
        NuGetPackageResolver resolver = new(new NuGetPackageResolverOptions(), store);

        Func<Task> act = () => resolver.ResolveAsync(new PackageResolutionRequest(
            PackageId: "demo.tool",
            Policy: ToolVersionPolicy.CachedOnly,
            VersionConstraint: null,
            AllowPrerelease: false,
            ExplicitVersion: null), CancellationToken.None);

        _ = await act.Should().ThrowAsync<PackageNotFoundException>()
            .WithMessage("Cached pinned version '2.0.0-beta.1' is prerelease but prerelease versions are not allowed.");
    }

    /// <summary>Writes a NuGet.Config that clears all sources so any metadata query proves cache fall-through.</summary>
    private async Task<string> WriteEmptyNuGetConfigAsync()
    {
        var configPath = Path.Combine(_tempDirectory, "NuGet.Config");
        await File.WriteAllTextAsync(configPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);
        return configPath;
    }

    /// <summary>Saves a single pinned package entry into the test cache index.</summary>
    private static Task SavePinnedAsync(
        ToolCacheIndexStore store,
        string pinnedVersion,
        string? versionConstraint,
        bool allowPrerelease)
    {
        return store.SaveAsync(new ToolCacheIndex
        {
            SchemaVersion = ToolCacheIndex.CurrentSchemaVersion,
            Packages = new Dictionary<string, ToolPackageState>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo.tool"] = new()
                {
                    KnownCachedVersions = new[] { pinnedVersion },
                    PinnedVersion = pinnedVersion,
                    LastLatestCheckUtc = DateTimeOffset.UtcNow,
                    VersionConstraint = versionConstraint,
                    AllowPrerelease = allowPrerelease,
                },
            },
        });
    }
}
