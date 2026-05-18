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

        ToolCacheIndex index = await _indexStore.LoadAsync(ct).ConfigureAwait(false);
        _ = index.Packages.TryGetValue(indexKey, out ToolPackageState? cachedState);

        switch (request.Policy)
        {
            case ToolVersionPolicy.CachedOnly:
            {
                if (cachedState is null)
                {
                    throw new PackageNotFoundException(packageId,
                        "CachedOnly policy requires a previously cached version but the index has no entry.");
                }
                if (!NuGetVersion.TryParse(cachedState.PinnedVersion, out NuGetVersion? cached) || cached is null)
                {
                    throw new PackageNotFoundException(packageId,
                        $"Cached pinned version '{cachedState.PinnedVersion}' is not a valid NuGet version.");
                }
                return new PackageResolution(packageId, cached, "cache", WasCacheHit: true);
            }

            case ToolVersionPolicy.CachedWithRefresh:
            {
                var withinTtl = cachedState is not null
                                 && DateTimeOffset.UtcNow - cachedState.LastLatestCheckUtc < _options.RefreshTtl;
                if (cachedState is not null && withinTtl
                    && NuGetVersion.TryParse(cachedState.PinnedVersion, out NuGetVersion? cached) && cached is not null)
                {
                    return new PackageResolution(packageId, cached, "cache", WasCacheHit: true);
                }
                return await QueryLatestAndUpdateIndexAsync(packageId, indexKey, request, cachedState, ct).ConfigureAwait(false);
            }

            case ToolVersionPolicy.AlwaysLatest:
            default:
                return await QueryLatestAndUpdateIndexAsync(packageId, indexKey, request, cachedState, ct).ConfigureAwait(false);
        }
    }

    private async Task<PackageResolution> QueryLatestAndUpdateIndexAsync(
        string packageId,
        string indexKey,
        PackageResolutionRequest request,
        ToolPackageState? previousState,
        CancellationToken ct)
    {
        if (_sources.Count == 0)
        {
            throw new InvalidOperationException("No NuGet sources are configured. Check NuGet.Config or the supplied options.");
        }

        VersionRange? constraint = null;
        if (!string.IsNullOrWhiteSpace(request.VersionConstraint)
            && !VersionRange.TryParse(request.VersionConstraint, out constraint))
        {
            throw new ArgumentException($"VersionConstraint '{request.VersionConstraint}' is not a valid NuGet version range.", nameof(request));
        }

        using SourceCacheContext cacheContext = new();
        NuGetVersion? best = null;
        string? bestSource = null;

        foreach (SourceRepository source in _sources)
        {
            try
            {
                MetadataResource metadataResource = await source.GetResourceAsync<MetadataResource>(ct).ConfigureAwait(false);

                IEnumerable<NuGetVersion> versions = await metadataResource
                    .GetVersions(packageId, request.AllowPrerelease, includeUnlisted: false, cacheContext, _nugetLogger, ct)
                    .ConfigureAwait(false);

                NuGetVersion? candidate = versions
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
            List<string> known = previousState?.KnownCachedVersions.ToList() ?? new List<string>();
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

    private static List<SourceRepository> BuildSources(NuGetPackageResolverOptions options)
    {
        ISettings settings = string.IsNullOrEmpty(options.NuGetConfigFile)
            ? Settings.LoadDefaultSettings(root: null)
            : Settings.LoadSpecificSettings(Path.GetDirectoryName(options.NuGetConfigFile) ?? Environment.CurrentDirectory,
                                            Path.GetFileName(options.NuGetConfigFile));

        PackageSourceProvider provider = new(settings);
        List<PackageSource> packageSources;

        if (!string.IsNullOrEmpty(options.RestrictToSource))
        {
            packageSources = new List<PackageSource> { new("restricted", options.RestrictToSource) };
        }
        else
        {
            packageSources = provider.LoadPackageSources().Where(s => s.IsEnabled).ToList();
            foreach (var extra in options.AdditionalSources)
            {
                if (!packageSources.Any(s => string.Equals(s.Source, extra, StringComparison.OrdinalIgnoreCase)))
                {
                    packageSources.Add(new PackageSource($"additional-{packageSources.Count}", extra));
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
        public NuGetLoggerAdapter(ILogger inner) => _inner = inner;

        public override void Log(NuGetLogMessage message)
        {
            LogLevel level = message.Level switch
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
