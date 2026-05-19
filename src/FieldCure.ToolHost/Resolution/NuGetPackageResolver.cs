using Microsoft.Extensions.Logging;

using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

using INuGetLogger = NuGet.Common.ILogger;
using NuGetLoggerBase = NuGet.Common.LoggerBase;
using NuGetLogLevel = NuGet.Common.LogLevel;
using NuGetLogMessage = NuGet.Common.ILogMessage;

namespace FieldCure.ToolHost.Resolution;

/// <summary>
/// Resolver that queries NuGet sources (via <c>NuGet.Protocol</c>) and falls back to the
/// persisted cache index under the policies in <see cref="ToolVersionPolicy"/>.
/// </summary>
public sealed class NuGetPackageResolver : IPackageResolver
{
    /// <summary>Default TTL applied to <see cref="ToolVersionPolicy.CachedWithRefresh"/>: 24 hours.</summary>
    public static readonly TimeSpan DefaultRefreshTtl = TimeSpan.FromHours(24);

    private readonly NuGetPackageResolverOptions _options;
    private readonly ToolCacheIndexStore _indexStore;
    private readonly ILogger<NuGetPackageResolver> _logger;
    private readonly INuGetLogger _nugetLogger;
    private readonly List<SourceRepository> _sources;

    /// <summary>Constructs a resolver using <paramref name="options"/> for source/TTL configuration.</summary>
    /// <param name="options">Source and cache settings.</param>
    /// <param name="indexStore">Persistent index used for cache hits and timestamps.</param>
    /// <param name="logger">Optional logger.</param>
    public NuGetPackageResolver(
        NuGetPackageResolverOptions options,
        ToolCacheIndexStore indexStore,
        ILogger<NuGetPackageResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(indexStore);

        _options = options;
        _indexStore = indexStore;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NuGetPackageResolver>.Instance;
        _nugetLogger = new NuGetLoggerAdapter(_logger);
        _sources = BuildSources(options);
    }

    /// <inheritdoc />
    public async Task<PackageResolution> ResolveAsync(PackageResolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);

        var packageId = request.PackageId;
        var indexKey = packageId.ToLowerInvariant();

        if (request.ExplicitVersion is not null)
        {
            return new PackageResolution(packageId, request.ExplicitVersion, "explicit", WasCacheHit: false);
        }

        var index = await _indexStore.LoadAsync(ct).ConfigureAwait(false);
        _ = index.Packages.TryGetValue(indexKey, out var cachedState);
        var constraint = ParseVersionConstraint(request);

        switch (request.Policy)
        {
            case ToolVersionPolicy.CachedOnly:
                {
                    if (cachedState is null)
                    {
                        throw new PackageNotFoundException(packageId,
                            "CachedOnly policy requires a previously cached version but the index has no entry.");
                    }
                    if (!NuGetVersion.TryParse(cachedState.PinnedVersion, out var cached) || cached is null)
                    {
                        throw new PackageNotFoundException(packageId,
                            $"Cached pinned version '{cachedState.PinnedVersion}' is not a valid NuGet version.");
                    }
                    if (!CachedVersionMatchesRequest(cached, constraint, request.AllowPrerelease))
                    {
                        throw new PackageNotFoundException(packageId,
                            BuildCachedVersionMismatchMessage(cachedState.PinnedVersion, request));
                    }
                    return new PackageResolution(packageId, cached, "cache", WasCacheHit: true);
                }

            case ToolVersionPolicy.CachedWithRefresh:
                {
                    var withinTtl = cachedState is not null
                                     && DateTimeOffset.UtcNow - cachedState.LastLatestCheckUtc < _options.RefreshTtl;
                    if (cachedState is not null && withinTtl
                        && NuGetVersion.TryParse(cachedState.PinnedVersion, out var cached) && cached is not null
                        && CachedVersionMatchesRequest(cached, constraint, request.AllowPrerelease))
                    {
                        return new PackageResolution(packageId, cached, "cache", WasCacheHit: true);
                    }
                    return await QueryLatestAndUpdateIndexAsync(packageId, indexKey, request, cachedState, constraint, ct).ConfigureAwait(false);
                }

            case ToolVersionPolicy.AlwaysLatest:
            default:
                return await QueryLatestAndUpdateIndexAsync(packageId, indexKey, request, cachedState, constraint, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Queries configured NuGet sources for the best version satisfying the request and persists the result to the cache index.
    /// </summary>
    private async Task<PackageResolution> QueryLatestAndUpdateIndexAsync(
        string packageId,
        string indexKey,
        PackageResolutionRequest request,
        ToolPackageState? previousState,
        VersionConstraintFilter? constraint,
        CancellationToken ct)
    {
        if (_sources.Count == 0)
        {
            throw new InvalidOperationException("No NuGet sources are configured. Check NuGet.Config or the supplied options.");
        }

        using SourceCacheContext cacheContext = new();
        NuGetVersion? best = null;
        string? bestSource = null;

        foreach (var source in _sources)
        {
            try
            {
                var metadataResource = await source.GetResourceAsync<MetadataResource>(ct).ConfigureAwait(false);

                var versions = await metadataResource
                    .GetVersions(packageId, request.AllowPrerelease, includeUnlisted: false, cacheContext, _nugetLogger, ct)
                    .ConfigureAwait(false);

                var candidate = versions
                    .Where(v => request.AllowPrerelease || !v.IsPrerelease)
                    .Where(v => constraint is null || constraint.Satisfies(v))
                    .DefaultIfEmpty(null)
                    .Max();

                if (candidate is not null && (best is null || candidate > best))
                {
                    best = candidate;
                    bestSource = source.PackageSource.Source;
                }
            }
            catch (FatalProtocolException ex) when (_options.IgnoreFailedSources)
            {
                _logger.LogWarning(ex, "Ignoring failed NuGet source {Source}", source.PackageSource.Source);
            }
        }

        if (best is null || bestSource is null)
        {
            throw new PackageNotFoundException(packageId,
                $"Package '{packageId}' was not found on any configured NuGet source.");
        }

        await _indexStore.UpdateAsync(current =>
        {
            Dictionary<string, ToolPackageState> next = new(current.Packages, StringComparer.OrdinalIgnoreCase);
            var known = previousState?.KnownCachedVersions.ToList() ?? [];
            var normalized = best.ToNormalizedString();
            if (!known.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                known.Insert(0, normalized);
            }
            next[indexKey] = new ToolPackageState
            {
                KnownCachedVersions = known,
                PinnedVersion = normalized,
                LastLatestCheckUtc = DateTimeOffset.UtcNow,
                VersionConstraint = request.VersionConstraint,
                AllowPrerelease = request.AllowPrerelease,
            };
            return new ToolCacheIndex { SchemaVersion = ToolCacheIndex.CurrentSchemaVersion, Packages = next };
        }, ct).ConfigureAwait(false);

        return new PackageResolution(packageId, best, bestSource, WasCacheHit: false);
    }

    /// <summary>
    /// Returns whether a pinned cache version is valid for the current prerelease and version-constraint request.
    /// </summary>
    internal static bool CachedVersionMatchesRequest(
        NuGetVersion cached,
        VersionConstraintFilter? constraint,
        bool allowPrerelease)
    {
        ArgumentNullException.ThrowIfNull(cached);

        if (!allowPrerelease && cached.IsPrerelease)
        {
            return false;
        }

        return constraint is null || constraint.Satisfies(cached);
    }

    /// <summary>
    /// Parses the request's optional version constraint into the internal predicate used by metadata and cache filtering.
    /// </summary>
    private static VersionConstraintFilter? ParseVersionConstraint(PackageResolutionRequest request)
    {
        var raw = request.VersionConstraint?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TryParseFloatingConstraint(raw, out var floatingConstraint))
        {
            return floatingConstraint;
        }

        if (!VersionRange.TryParse(raw, out var constraint))
        {
            throw new ArgumentException($"VersionConstraint '{request.VersionConstraint}' is not a valid NuGet version range.", nameof(request));
        }

        return new VersionConstraintFilter(constraint);
    }

    /// <summary>
    /// Parses simple floating constraints such as <c>2.*</c> and <c>2.1.*</c>.
    /// </summary>
    private static bool TryParseFloatingConstraint(string raw, out VersionConstraintFilter? constraint)
    {
        constraint = null;
        if (!raw.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = raw.Split('.');
        if (parts.Length is < 2 or > 3 || !string.Equals(parts[^1], "*", StringComparison.Ordinal))
        {
            throw new ArgumentException($"VersionConstraint '{raw}' is not a supported floating version constraint.", nameof(raw));
        }

        List<int> prefix = [];
        foreach (string part in parts[..^1])
        {
            if (!int.TryParse(part, out int value) || value < 0)
            {
                throw new ArgumentException($"VersionConstraint '{raw}' is not a supported floating version constraint.", nameof(raw));
            }
            prefix.Add(value);
        }

        constraint = new VersionConstraintFilter(prefix.ToArray());
        return true;
    }

    /// <summary>
    /// Predicate wrapper for NuGet version ranges and the simple floating constraints ToolHost documents.
    /// </summary>
    internal sealed class VersionConstraintFilter
    {
        private readonly VersionRange? _range;
        private readonly IReadOnlyList<int>? _floatingPrefix;

        /// <summary>Creates a filter backed by a NuGet version range.</summary>
        public VersionConstraintFilter(VersionRange range)
        {
            ArgumentNullException.ThrowIfNull(range);
            _range = range;
        }

        /// <summary>Creates a filter backed by a major or major/minor floating-version prefix.</summary>
        public VersionConstraintFilter(IReadOnlyList<int> floatingPrefix)
        {
            ArgumentNullException.ThrowIfNull(floatingPrefix);
            _floatingPrefix = floatingPrefix;
        }

        /// <summary>Returns whether <paramref name="version"/> satisfies this filter.</summary>
        public bool Satisfies(NuGetVersion version)
        {
            ArgumentNullException.ThrowIfNull(version);

            if (_floatingPrefix is not null)
            {
                return _floatingPrefix.Count switch
                {
                    1 => version.Major == _floatingPrefix[0],
                    2 => version.Major == _floatingPrefix[0] && version.Minor == _floatingPrefix[1],
                    _ => false,
                };
            }

            return _range is null || _range.Satisfies(version);
        }
    }

    /// <summary>Builds a diagnostic message explaining why a cached version cannot satisfy the current request.</summary>
    private static string BuildCachedVersionMismatchMessage(string cachedVersion, PackageResolutionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VersionConstraint))
        {
            return $"Cached pinned version '{cachedVersion}' does not satisfy VersionConstraint '{request.VersionConstraint}'.";
        }

        if (!request.AllowPrerelease)
        {
            return $"Cached pinned version '{cachedVersion}' is prerelease but prerelease versions are not allowed.";
        }

        return $"Cached pinned version '{cachedVersion}' does not satisfy the current request.";
    }

    /// <summary>Builds source repositories from NuGet settings plus any explicit source overrides.</summary>
    private static List<SourceRepository> BuildSources(NuGetPackageResolverOptions options)
    {
        var settings = string.IsNullOrEmpty(options.NuGetConfigFile)
            ? Settings.LoadDefaultSettings(root: null)
            : Settings.LoadSpecificSettings(Path.GetDirectoryName(options.NuGetConfigFile) ?? Environment.CurrentDirectory,
                                            Path.GetFileName(options.NuGetConfigFile));

        PackageSourceProvider provider = new(settings);
        List<PackageSource> packageSources;

        if (!string.IsNullOrEmpty(options.RestrictToSource))
        {
            // PackageSource ctor is (source, name) — first arg is the feed URL,
            // second is the display name. Reversing them yields a SourceRepository
            // backed by the literal string "restricted", which silently returns
            // empty version lists and surfaces as PackageNotFoundException.
            packageSources = [new(options.RestrictToSource, "restricted")];
        }
        else
        {
            packageSources = provider.LoadPackageSources().Where(s => s.IsEnabled).ToList();
            foreach (var extra in options.AdditionalSources)
            {
                if (!packageSources.Any(s => string.Equals(s.Source, extra, StringComparison.OrdinalIgnoreCase)))
                {
                    packageSources.Add(new PackageSource(extra, $"additional-{packageSources.Count}"));
                }
            }
        }

        return packageSources
            .Select(ps => Repository.Factory.GetCoreV3(ps))
            .ToList();
    }

    private sealed class NuGetLoggerAdapter : NuGetLoggerBase
    {
        private readonly ILogger _inner;

        /// <summary>Creates an adapter that forwards NuGet log messages to <see cref="ILogger"/>.</summary>
        public NuGetLoggerAdapter(ILogger inner) => _inner = inner;

        /// <summary>Translates and forwards a NuGet log message synchronously.</summary>
        public override void Log(NuGetLogMessage message)
        {
            var level = message.Level switch
            {
                NuGetLogLevel.Debug => LogLevel.Debug,
                NuGetLogLevel.Verbose => LogLevel.Debug,
                NuGetLogLevel.Information => LogLevel.Information,
                NuGetLogLevel.Minimal => LogLevel.Information,
                NuGetLogLevel.Warning => LogLevel.Warning,
                NuGetLogLevel.Error => LogLevel.Error,
                _ => LogLevel.Information,
            };
            _inner.Log(level, "{Message}", message.Message);
        }

        /// <summary>Translates and forwards a NuGet log message asynchronously.</summary>
        public override Task LogAsync(NuGetLogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Options for <see cref="NuGetPackageResolver"/>.</summary>
public sealed record NuGetPackageResolverOptions
{
    /// <summary>Restricts resolution to a single source URI. Equivalent to <c>dnx --source</c>.</summary>
    public string? RestrictToSource { get; init; }

    /// <summary>Additional NuGet source URIs. Equivalent to <c>dnx --add-source</c>.</summary>
    public IReadOnlyList<string> AdditionalSources { get; init; } = Array.Empty<string>();

    /// <summary>Optional path to a specific <c>NuGet.Config</c> file. When null, uses default discovery.</summary>
    public string? NuGetConfigFile { get; init; }

    /// <summary>TTL applied to <see cref="ToolVersionPolicy.CachedWithRefresh"/>.</summary>
    public TimeSpan RefreshTtl { get; init; } = NuGetPackageResolver.DefaultRefreshTtl;

    /// <summary>When true, unreachable sources are logged and skipped instead of aborting resolution.</summary>
    public bool IgnoreFailedSources { get; init; }
}

/// <summary>Thrown when a requested package cannot be located.</summary>
public sealed class PackageNotFoundException : Exception
{
    /// <summary>The package ID that was requested.</summary>
    public string PackageId { get; }

    /// <summary>Constructs a new exception with package id and message.</summary>
    public PackageNotFoundException(string packageId, string message) : base(message)
    {
        PackageId = packageId;
    }

    /// <summary>Constructs a new exception with package id, message, and inner exception.</summary>
    public PackageNotFoundException(string packageId, string message, Exception inner) : base(message, inner)
    {
        PackageId = packageId;
    }
}
