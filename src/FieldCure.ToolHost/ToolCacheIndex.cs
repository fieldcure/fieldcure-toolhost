namespace FieldCure.ToolHost;

/// <summary>
/// Persistent metadata cache for tools managed by <see cref="DnxLiteRunner"/>.
/// Stored as JSON at <c>{MetadataRoot}/_index.json</c>.
/// </summary>
public sealed record ToolCacheIndex
{
    /// <summary>Current schema version emitted by this version of the library.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version. Incremented when the persisted format changes in a breaking way.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Per-package state, keyed by lower-cased NuGet package id.</summary>
    public required Dictionary<string, ToolPackageState> Packages { get; init; }

    /// <summary>Creates an empty index at the current schema version.</summary>
    public static ToolCacheIndex Empty() =>
        new() { SchemaVersion = CurrentSchemaVersion, Packages = new Dictionary<string, ToolPackageState>(StringComparer.OrdinalIgnoreCase) };
}

/// <summary>Per-package metadata persisted across sessions.</summary>
public sealed record ToolPackageState
{
    /// <summary>
    /// Versions ToolHost has previously verified locally, newest first.
    /// The source-of-truth for existence is still the NuGet global cache directory —
    /// this list is a hint, validated on access.
    /// </summary>
    public required IReadOnlyList<string> KnownCachedVersions { get; init; }

    /// <summary>The version that will be used on the next launch under cache-friendly policies.</summary>
    public required string PinnedVersion { get; init; }

    /// <summary>UTC timestamp of the last successful NuGet metadata query. Drives TTL for <see cref="ToolVersionPolicy.CachedWithRefresh"/>.</summary>
    public required DateTimeOffset LastLatestCheckUtc { get; init; }

    /// <summary>Optional user-supplied version constraint (e.g., <c>"[1.2.0,2.0.0)"</c>). Null = follow latest stable.</summary>
    public string? VersionConstraint { get; init; }

    /// <summary>Whether to allow prerelease versions when resolving latest.</summary>
    public bool AllowPrerelease { get; init; }
}
