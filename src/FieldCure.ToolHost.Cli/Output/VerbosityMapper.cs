using Microsoft.Extensions.Logging;

namespace FieldCure.ToolHost.Cli.Output;

/// <summary>
/// Maps the <c>--verbosity</c> CLI flag (dnx-compatible levels) to <see cref="LogLevel"/>.
/// </summary>
public static class VerbosityMapper
{
    /// <summary>Levels accepted on the command line.</summary>
    public static readonly string[] AcceptedLevels =
    {
        "q", "quiet",
        "m", "minimal",
        "n", "normal",
        "d", "detailed",
        "diag", "diagnostic",
    };

    /// <summary>Default verbosity when the user does not pass <c>--verbosity</c>.</summary>
    public const string DefaultLevel = "normal";

    /// <summary>Converts a verbosity string to a <see cref="LogLevel"/>.</summary>
    /// <param name="verbosity">Accepted aliases: <c>q[uiet]</c>, <c>m[inimal]</c>, <c>n[ormal]</c>, <c>d[etailed]</c>, <c>diag[nostic]</c>.</param>
    public static LogLevel Map(string? verbosity) => (verbosity ?? DefaultLevel).ToLowerInvariant() switch
    {
        "q" or "quiet" => LogLevel.None,
        "m" or "minimal" => LogLevel.Warning,
        "n" or "normal" => LogLevel.Information,
        "d" or "detailed" => LogLevel.Debug,
        "diag" or "diagnostic" => LogLevel.Trace,
        _ => LogLevel.Information,
    };
}
