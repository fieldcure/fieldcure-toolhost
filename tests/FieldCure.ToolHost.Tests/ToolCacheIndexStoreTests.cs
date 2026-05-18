using FluentAssertions;

namespace FieldCure.ToolHost.Tests;

public sealed class ToolCacheIndexStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _indexPath;

    public ToolCacheIndexStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FieldCure-ToolHost-Tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        using ToolCacheIndexStore store = new(_indexPath);
        ToolCacheIndex index = await store.LoadAsync();
        _ = index.Packages.Should().BeEmpty();
        _ = index.SchemaVersion.Should().Be(ToolCacheIndex.CurrentSchemaVersion);
    }

    [Xunit.Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmpty()
    {
        await File.WriteAllTextAsync(_indexPath, "{ not json");
        using ToolCacheIndexStore store = new(_indexPath);
        ToolCacheIndex index = await store.LoadAsync();
        _ = index.Packages.Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task SaveAsync_LoadAsync_RoundTripsState()
    {
        using ToolCacheIndexStore store = new(_indexPath);

        ToolCacheIndex original = new()
        {
            SchemaVersion = ToolCacheIndex.CurrentSchemaVersion,
            Packages = new Dictionary<string, ToolPackageState>(StringComparer.OrdinalIgnoreCase)
            {
                ["dotnetsay"] = new()
                {
                    KnownCachedVersions = new[] { "3.0.3", "3.0.2" },
                    PinnedVersion = "3.0.3",
                    LastLatestCheckUtc = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero),
                    AllowPrerelease = false,
                },
            },
        };

        await store.SaveAsync(original);
        ToolCacheIndex loaded = await store.LoadAsync();

        _ = loaded.Packages.Should().ContainKey("dotnetsay");
        ToolPackageState state = loaded.Packages["dotnetsay"];
        _ = state.PinnedVersion.Should().Be("3.0.3");
        _ = state.KnownCachedVersions.Should().Equal("3.0.3", "3.0.2");
        _ = state.LastLatestCheckUtc.Should().Be(new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero));
    }

    [Xunit.Fact]
    public async Task SaveAsync_OverwritesAtomically()
    {
        using ToolCacheIndexStore store = new(_indexPath);

        await store.SaveAsync(ToolCacheIndex.Empty());
        await store.SaveAsync(new ToolCacheIndex
        {
            SchemaVersion = ToolCacheIndex.CurrentSchemaVersion,
            Packages = new Dictionary<string, ToolPackageState>(StringComparer.OrdinalIgnoreCase)
            {
                ["pkg"] = new()
                {
                    KnownCachedVersions = Array.Empty<string>(),
                    PinnedVersion = "1.0.0",
                    LastLatestCheckUtc = DateTimeOffset.UtcNow,
                },
            },
        });

        _ = File.Exists(_indexPath + ".tmp").Should().BeFalse("the staging file should be renamed away");
        ToolCacheIndex loaded = await store.LoadAsync();
        _ = loaded.Packages.Should().ContainKey("pkg");
    }

    [Xunit.Fact]
    public async Task UpdateAsync_AppliesMutatorAndPersists()
    {
        using ToolCacheIndexStore store = new(_indexPath);

        ToolCacheIndex result = await store.UpdateAsync(current =>
        {
            Dictionary<string, ToolPackageState> next = new(current.Packages, StringComparer.OrdinalIgnoreCase)
            {
                ["new.pkg"] = new()
                {
                    KnownCachedVersions = new[] { "1.0.0" },
                    PinnedVersion = "1.0.0",
                    LastLatestCheckUtc = DateTimeOffset.UtcNow,
                },
            };
            return new ToolCacheIndex { SchemaVersion = ToolCacheIndex.CurrentSchemaVersion, Packages = next };
        });

        _ = result.Packages.Should().ContainKey("new.pkg");
        ToolCacheIndex reloaded = await store.LoadAsync();
        _ = reloaded.Packages.Should().ContainKey("new.pkg");
    }
}
