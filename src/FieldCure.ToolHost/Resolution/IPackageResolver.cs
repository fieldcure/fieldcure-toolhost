using NuGet.Versioning;

namespace FieldCure.ToolHost.Resolution;

/// <summary>
/// Resolves an abstract tool reference (package id + policy + constraint) to a concrete
/// NuGet version. Does not download anything by itself.
/// </summary>
public interface IPackageResolver
{
    /// <summary>
    /// Resolves the target version for the given package ID and policy.
    /// Honors NuGet source configuration and credential providers transparently.
    /// </summary>
    /// <param name="request">Resolution inputs (package id, policy, constraint, prerelease flag).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved version plus the source where it was found.</returns>
    Task<PackageResolution> ResolveAsync(PackageResolutionRequest request, CancellationToken ct);
}

/// <summary>Inputs to <see cref="IPackageResolver.ResolveAsync"/>.</summary>
/// <param name="PackageId">NuGet package ID.</param>
/// <param name="Policy">Caching/refresh policy.</param>
/// <param name="VersionConstraint">Optional version range (NuGet syntax). Null means "latest matching policy".</param>
/// <param name="AllowPrerelease">Whether to include prerelease versions in the search.</param>
/// <param name="ExplicitVersion">When non-null, bypasses policy and uses this version verbatim.</param>
public sealed record PackageResolutionRequest(
    string PackageId,
    ToolVersionPolicy Policy,
    string? VersionConstraint,
    bool AllowPrerelease,
    NuGetVersion? ExplicitVersion);

/// <summary>Outcome of a resolution.</summary>
/// <param name="PackageId">Package ID (preserves caller casing for display; comparisons use OrdinalIgnoreCase elsewhere).</param>
/// <param name="Version">Resolved version.</param>
/// <param name="SourceUrl">URL of the source that supplied the version, or <c>"cache"</c> / <c>"explicit"</c>.</param>
/// <param name="WasCacheHit">True iff the resolution came from the local index without hitting the network.</param>
public sealed record PackageResolution(
    string PackageId,
    NuGetVersion Version,
    string SourceUrl,
    bool WasCacheHit);
