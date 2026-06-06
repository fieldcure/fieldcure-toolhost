using System.Diagnostics;

using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;

using Microsoft.Extensions.Logging;

using NuGet.Versioning;

namespace FieldCure.ToolHost;

/// <summary>
/// Primary public API. Orchestrates resolution, extraction, and launch of a .NET tool
/// from NuGet without requiring the .NET SDK.
/// </summary>
public sealed class DnxLiteRunner
{
    private readonly DotnetEnvironment _environment;
    private readonly IPackageResolver _resolver;
    private readonly IToolExtractor _extractor;
    private readonly IToolLauncher _launcher;
    private readonly ILogger<DnxLiteRunner> _logger;

    /// <summary>
    /// Constructs a runner with default services (NuGet-backed resolver, NuGet-cache extractor,
    /// process launcher) wired to the supplied environment and default options.
    /// </summary>
    /// <param name="environment">Detected host environment.</param>
    /// <param name="logger">Optional logger.</param>
    public DnxLiteRunner(DotnetEnvironment environment, ILogger<DnxLiteRunner>? logger = null)
        : this(environment, BuildDefaultResolver(environment), new NuGetToolExtractor(environment), new ToolLauncher(), logger)
    {
    }

    /// <summary>
    /// Constructs a runner with explicit dependencies. Used by tests and advanced embedders.
    /// </summary>
    /// <param name="environment">Detected host environment.</param>
    /// <param name="resolver">Package resolver.</param>
    /// <param name="extractor">Tool extractor.</param>
    /// <param name="launcher">Tool launcher.</param>
    /// <param name="logger">Optional logger.</param>
    public DnxLiteRunner(
        DotnetEnvironment environment,
        IPackageResolver resolver,
        IToolExtractor extractor,
        IToolLauncher launcher,
        ILogger<DnxLiteRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(launcher);

        _environment = environment;
        _resolver = resolver;
        _extractor = extractor;
        _launcher = launcher;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DnxLiteRunner>.Instance;
    }

    /// <summary>The environment captured at construction. Exposed for diagnostics.</summary>
    public DotnetEnvironment Environment => _environment;

    /// <summary>
    /// Resolves, downloads if necessary, and launches the specified tool.
    /// </summary>
    /// <param name="request">Tool invocation parameters.</param>
    /// <param name="ct">
    /// Cancellation token honored during resolution and download. Process launch is not
    /// cancellable once initiated.
    /// </param>
    /// <returns>The started <see cref="Process"/>. Caller manages stdio and disposal.</returns>
    public async Task<Process> StartAsync(ToolInvocationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);

        _logger.LogDebug("Resolving package {PackageId} with policy {Policy}", request.PackageId, request.Policy);

        PackageResolutionRequest resolutionRequest = new(
            PackageId: request.PackageId,
            Policy: request.Policy,
            VersionConstraint: request.VersionConstraint,
            AllowPrerelease: request.AllowPrerelease,
            ExplicitVersion: request.ExplicitVersion);

        var resolution = await _resolver.ResolveAsync(resolutionRequest, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Resolved {PackageId} -> {Version} (source={Source}, cacheHit={CacheHit})",
            resolution.PackageId, resolution.Version.ToNormalizedString(), resolution.SourceUrl, resolution.WasCacheHit);

        var layout = await _extractor.EnsureExtractedAsync(resolution, ct).ConfigureAwait(false);

        LaunchRequest launchRequest = new()
        {
            Layout = layout,
            Arguments = request.ToolArguments,
            DotnetMuxerPath = _environment.DotnetMuxerPath,
            AllowRollForward = request.AllowRollForward,
            InheritEnvironmentVariables = request.InheritEnvironmentVariables,
            AdditionalEnvironment = request.AdditionalEnvironment,
        };

        return _launcher.Start(launchRequest);
    }

    /// <summary>Constructs the default <see cref="NuGetPackageResolver"/> with the standard cache index store and default options.</summary>
    private static NuGetPackageResolver BuildDefaultResolver(DotnetEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ToolCacheIndexStore store = new();
        NuGetPackageResolverOptions options = new();
        return new NuGetPackageResolver(options, store);
    }
}

/// <summary>Inputs to <see cref="DnxLiteRunner.StartAsync"/>.</summary>
public sealed record ToolInvocationRequest
{
    /// <summary>NuGet package ID of the tool to run.</summary>
    public required string PackageId { get; init; }

    /// <summary>Arguments forwarded to the tool's entry point.</summary>
    public required IReadOnlyList<string> ToolArguments { get; init; }

    /// <summary>Version resolution policy. Ignored when <see cref="ExplicitVersion"/> is set.</summary>
    public ToolVersionPolicy Policy { get; init; } = ToolVersionPolicy.CachedWithRefresh;

    /// <summary>Explicit version override. Bypasses policy entirely when non-null.</summary>
    public NuGetVersion? ExplicitVersion { get; init; }

    /// <summary>Whether to include prerelease versions when resolving latest.</summary>
    public bool AllowPrerelease { get; init; }

    /// <summary>Whether to allow .NET runtime roll-forward to a newer major version.</summary>
    public bool AllowRollForward { get; init; }

    /// <summary>
    /// Gets a value indicating whether the launched tool inherits the current process environment.
    /// Defaults to <see langword="true"/> to match normal process and <c>dnx</c> behavior.
    /// </summary>
    /// <remarks>
    /// Set this to <see langword="false"/> when launching untrusted tools or MCP servers that should
    /// only receive explicitly supplied environment variables. Hosts that disable inheritance should
    /// usually seed <see cref="AdditionalEnvironment"/> with
    /// <see cref="ToolEnvironment.GetDefaultEnvironmentVariables"/> and then add tool-specific secrets.
    /// </remarks>
    public bool InheritEnvironmentVariables { get; init; } = true;

    /// <summary>Additional NuGet source URIs beyond <c>NuGet.Config</c>. Equivalent to <c>dnx --add-source</c>.</summary>
    public IReadOnlyList<string>? AdditionalSources { get; init; }

    /// <summary>Restrict resolution to a single source. Equivalent to <c>dnx --source</c>.</summary>
    public string? RestrictToSource { get; init; }

    /// <summary>
    /// Optional NuGet version range to constrain resolution (e.g. <c>"2.*"</c>, <c>"[2.0.0,3.0.0)"</c>).
    /// Ignored when <see cref="ExplicitVersion"/> is set. When null, the resolver picks the latest
    /// version permitted by <see cref="Policy"/> and <see cref="AllowPrerelease"/>.
    /// </summary>
    public string? VersionConstraint { get; init; }

    /// <summary>
    /// Optional environment variables to set on the child process. Values of <c>null</c> remove the variable.
    /// Forwarded to <see cref="Execution.LaunchRequest.AdditionalEnvironment"/> unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? AdditionalEnvironment { get; init; }
}
