using Microsoft.Extensions.Logging;

using NuGet.Credentials;

using INuGetLogger = NuGet.Common.ILogger;
using NuGetLogMessage = NuGet.Common.ILogMessage;
using NuGetLoggerBase = NuGet.Common.LoggerBase;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace FieldCure.ToolHost.Authentication;

/// <summary>
/// Wires up NuGet credential providers so authenticated feeds (Azure DevOps, GitHub Packages,
/// MyGet, etc.) work transparently. Must be called once during host initialization,
/// before any NuGet HTTP request.
/// </summary>
public static class CredentialProviderSetup
{
    private static int _registered;

    /// <summary>
    /// Discovers and registers credential providers from standard locations:
    /// <c>NUGET_PLUGIN_PATHS</c> env var, <c>~/.nuget/plugins</c>, and the dotnet CLI plugin folder.
    /// Idempotent — subsequent calls are no-ops with the original interactivity setting preserved.
    /// </summary>
    /// <param name="interactive">
    /// When true, providers may prompt the user (device login, etc.). When false,
    /// providers must operate non-interactively or fail.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public static void Register(bool interactive, ILogger? logger = null)
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        var nugetLogger = logger is null
            ? NuGet.Common.NullLogger.Instance
            : new ForwardingLogger(logger);

        DefaultCredentialServiceUtility.SetupDefaultCredentialService(nugetLogger, nonInteractive: !interactive);
    }

    /// <summary>Adapter that forwards <c>NuGet.Common.ILogger</c> credential-plugin messages to a <c>Microsoft.Extensions.Logging.ILogger</c>.</summary>
    private sealed class ForwardingLogger : NuGetLoggerBase
    {
        /// <summary>Sink for translated log messages.</summary>
        private readonly ILogger _inner;

        /// <summary>Creates an adapter wrapping <paramref name="inner"/>.</summary>
        public ForwardingLogger(ILogger inner) => _inner = inner;

        /// <summary>Translates a NuGet log level and forwards the message synchronously.</summary>
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

        /// <summary>Async overload — defers to the synchronous <see cref="Log"/>.</summary>
        public override Task LogAsync(NuGetLogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
