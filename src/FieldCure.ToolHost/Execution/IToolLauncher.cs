using System.Diagnostics;

using FieldCure.ToolHost.Extraction;

namespace FieldCure.ToolHost.Execution;

/// <summary>
/// Launches the entry point of an extracted tool as a child process with standard streams redirected.
/// </summary>
public interface IToolLauncher
{
    /// <summary>
    /// Starts the tool. Returns the started <see cref="Process"/> with stdin, stdout, and stderr
    /// redirected. The caller is responsible for reading/writing the streams and disposing the process.
    /// </summary>
    /// <param name="request">Launch parameters.</param>
    /// <exception cref="ToolLaunchFailedException">Thrown when the process cannot be started.</exception>
    Process Start(LaunchRequest request);
}

/// <summary>Inputs to <see cref="IToolLauncher.Start"/>.</summary>
public sealed record LaunchRequest
{
    /// <summary>Extracted package layout supplied by <see cref="IToolExtractor"/>.</summary>
    public required ExtractedToolLayout Layout { get; init; }

    /// <summary>Arguments forwarded to the tool's entry point.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Absolute path to the <c>dotnet</c> muxer used when the runner is <c>"dotnet"</c>.</summary>
    public required string DotnetMuxerPath { get; init; }

    /// <summary>When true, sets <c>DOTNET_ROLL_FORWARD=LatestMajor</c> on the child process.</summary>
    public bool AllowRollForward { get; init; }

    /// <summary>
    /// Gets a value indicating whether the launched tool inherits the current process environment.
    /// Defaults to <see langword="true"/> to match normal process and <c>dnx</c> behavior.
    /// </summary>
    public bool InheritEnvironmentVariables { get; init; } = true;

    /// <summary>
    /// Optional environment variables to set/clear on the child process.
    /// Values of <c>null</c> remove the variable.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? AdditionalEnvironment { get; init; }
}

/// <summary>Thrown when the host failed to spawn a tool's child process.</summary>
public sealed class ToolLaunchFailedException : Exception
{
    /// <summary>Constructs a new exception with a message.</summary>
    public ToolLaunchFailedException(string message) : base(message)
    {
    }

    /// <summary>Constructs a new exception with a message and inner exception.</summary>
    public ToolLaunchFailedException(string message, Exception inner) : base(message, inner)
    {
    }
}
