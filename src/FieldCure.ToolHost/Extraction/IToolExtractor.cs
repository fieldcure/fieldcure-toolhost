using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Resolution;

namespace FieldCure.ToolHost.Extraction;

/// <summary>
/// Downloads (if necessary) and extracts a resolved package into the NuGet global packages folder
/// using the standard NuGet layout. Idempotent — safe to call concurrently for the same package.
/// </summary>
public interface IToolExtractor
{
    /// <summary>
    /// Ensures the package is present in the NuGet global cache and returns the path to its
    /// <c>tools/{tfm}/{rid}/</c> directory matching the host.
    /// </summary>
    /// <param name="resolution">A resolved package (id + version + source).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="NoCompatibleToolException">Thrown when no compatible tools folder exists in the package.</exception>
    /// <exception cref="ExtractionFailedException">Thrown when download or extraction fails.</exception>
    Task<ExtractedToolLayout> EnsureExtractedAsync(PackageResolution resolution, CancellationToken ct);
}

/// <summary>The on-disk shape of an extracted .NET tool package.</summary>
/// <param name="PackagePath">Absolute path to the package folder (e.g., <c>~/.nuget/packages/pkg/1.2.3/</c>).</param>
/// <param name="ToolsFolder">Absolute path to the chosen <c>tools/{tfm}/{rid}/</c> folder.</param>
/// <param name="TargetFramework">Selected target framework moniker.</param>
/// <param name="RuntimeIdentifier">Selected runtime identifier (typically <c>"any"</c> for managed tools).</param>
/// <param name="ToolSettings">Parsed <c>DotnetToolSettings.xml</c> from the tools folder.</param>
public sealed record ExtractedToolLayout(
    string PackagePath,
    string ToolsFolder,
    string TargetFramework,
    string RuntimeIdentifier,
    DotnetToolSettings ToolSettings);

/// <summary>Thrown when a package was located but contains no compatible <c>tools/{tfm}/{rid}/</c> folder.</summary>
public sealed class NoCompatibleToolException : Exception
{
    /// <summary>The package ID that lacks a compatible tools folder.</summary>
    public string PackageId { get; }

    /// <summary>Constructs a new exception with package id and message.</summary>
    public NoCompatibleToolException(string packageId, string message) : base(message)
    {
        PackageId = packageId;
    }
}

/// <summary>Thrown when extracting a downloaded package fails.</summary>
public sealed class ExtractionFailedException : Exception
{
    /// <summary>Constructs a new exception with a message.</summary>
    public ExtractionFailedException(string message) : base(message)
    {
    }

    /// <summary>Constructs a new exception with a message and inner exception.</summary>
    public ExtractionFailedException(string message, Exception inner) : base(message, inner)
    {
    }
}
