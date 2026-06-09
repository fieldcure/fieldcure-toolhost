using System.CommandLine;
using System.Diagnostics;

using FieldCure.ToolHost.Authentication;
using FieldCure.ToolHost.Cli.Output;
using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;

using Microsoft.Extensions.Logging;

using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace FieldCure.ToolHost.Cli;

/// <summary>
/// Builds the <c>fcdnx</c> root command. Designed to mirror <c>dnx</c> / <c>dotnet tool execute</c>
/// flag-for-flag for the v0.1 surface defined in the specification.
/// </summary>
public static class DnxCommand
{
    /// <summary>Builds the configured <see cref="RootCommand"/>.</summary>
    public static RootCommand Build()
    {
        Argument<string> packageArg = new("PACKAGE_ID")
        {
            Description = "NuGet package ID of the tool. Append @VERSION for a specific version.",
        };

        Option<string?> versionOpt = new("--tool-package-version")
        {
            Description = "Explicit version of the tool package. Alias for the @VERSION suffix.",
        };
        Option<bool> yesOpt = new("--yes", "-y") { Description = "Skip download confirmation prompt." };
        Option<bool> prereleaseOpt = new("--prerelease") { Description = "Include prerelease versions when resolving latest." };
        Option<string?> sourceOpt = new("--source") { Description = "Restrict resolution to a single NuGet source." };
        Option<string[]> addSourceOpt = new("--add-source")
        {
            Description = "Additional NuGet source. May be repeated.",
            AllowMultipleArgumentsPerToken = false,
        };
        Option<string?> configFileOpt = new("--configfile") { Description = "Path to a NuGet.Config to use." };
        Option<bool> ignoreFailedSourcesOpt = new("--ignore-failed-sources") { Description = "Treat unavailable sources as warnings." };
        Option<bool> noCacheOpt = new("--no-cache") { Description = "Disable NuGet HTTP and memory caches." };
        Option<bool> noHttpCacheOpt = new("--no-http-cache") { Description = "Disable HTTP cache only." };
        Option<bool> interactiveOpt = new("--interactive") { Description = "Allow interactive credential prompts." };
        Option<bool> allowRollForwardOpt = new("--allow-roll-forward") { Description = "Permit .NET runtime roll-forward." };
        Option<bool> noInheritEnvOpt = new("--no-inherit-env")
        {
            Description = "Launch the tool without inheriting ambient environment variables.",
        };
        Option<string[]> envOpt = new("--env")
        {
            Description = "Set an environment variable for the launched tool (KEY=VALUE). May be repeated.",
            AllowMultipleArgumentsPerToken = false,
        };
        Option<string[]> unsetEnvOpt = new("--unset-env")
        {
            Description = "Remove an environment variable from the launched tool environment. May be repeated.",
            AllowMultipleArgumentsPerToken = false,
        };
        Option<string?> verbosityOpt = new("--verbosity")
        {
            Description = "Verbosity: q[uiet], m[inimal], n[ormal], d[etailed], diag[nostic].",
        };
        Option<string?> frameworkOpt = new("--framework") { Description = "Override target framework selection." };
        Option<string?> archOpt = new("--arch") { Description = "Override RID architecture (best-effort in v0.1)." };
        Option<string?> osOpt = new("--os") { Description = "Override RID OS (best-effort in v0.1)." };
        Option<string?> policyOpt = new("--policy")
        {
            Description = "Version policy: AlwaysLatest (default), CachedWithRefresh, CachedOnly.",
            DefaultValueFactory = _ => nameof(ToolVersionPolicy.AlwaysLatest),
        };

        RootCommand root = new(
            "fcdnx — Run .NET tools from NuGet without the .NET SDK. " +
            "A bridge for runtime-only environments until Microsoft ships standalone dnx " +
            "(https://github.com/dotnet/sdk/issues/49796).")
        {
            packageArg,
            versionOpt,
            yesOpt,
            prereleaseOpt,
            sourceOpt,
            addSourceOpt,
            configFileOpt,
            ignoreFailedSourcesOpt,
            noCacheOpt,
            noHttpCacheOpt,
            interactiveOpt,
            allowRollForwardOpt,
            noInheritEnvOpt,
            envOpt,
            unsetEnvOpt,
            verbosityOpt,
            frameworkOpt,
            archOpt,
            osOpt,
            policyOpt,
        };
        root.TreatUnmatchedTokensAsErrors = false;

        root.SetAction((parseResult, ct) => RunAsync(
            parseResult,
            packageArg,
            versionOpt,
            prereleaseOpt,
            sourceOpt,
            addSourceOpt,
            configFileOpt,
            ignoreFailedSourcesOpt,
            interactiveOpt,
            allowRollForwardOpt,
            noInheritEnvOpt,
            envOpt,
            unsetEnvOpt,
            verbosityOpt,
            policyOpt,
            ct));

        return root;
    }

    /// <summary>Main action handler. Parses options, builds the runner, launches the tool, forwards stdio, and maps exceptions to <see cref="ExitCodes"/>.</summary>
    private static async Task<int> RunAsync(
        ParseResult parseResult,
        Argument<string> packageArg,
        Option<string?> versionOpt,
        Option<bool> prereleaseOpt,
        Option<string?> sourceOpt,
        Option<string[]> addSourceOpt,
        Option<string?> configFileOpt,
        Option<bool> ignoreFailedSourcesOpt,
        Option<bool> interactiveOpt,
        Option<bool> allowRollForwardOpt,
        Option<bool> noInheritEnvOpt,
        Option<string[]> envOpt,
        Option<string[]> unsetEnvOpt,
        Option<string?> verbosityOpt,
        Option<string?> policyOpt,
        CancellationToken ct)
    {
        var verbosity = parseResult.GetValue(verbosityOpt) ?? VerbosityMapper.DefaultLevel;
        var minLevel = VerbosityMapper.Map(verbosity);

        using var loggerFactory = LoggerFactory.Create(b =>
        {
            _ = b.SetMinimumLevel(minLevel);
            // fcdnx's own diagnostics MUST go to stderr: when it hosts a stdio MCP server,
            // the child's stdout is the JSON-RPC channel that fcdnx forwards verbatim. Any
            // fcdnx log on stdout would interleave with JSON-RPC frames and corrupt the
            // protocol, so route every level to stderr. (The hosted server controls its
            // own streams and already logs to stderr.)
            _ = b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            _ = b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.IncludeScopes = false;
                o.TimestampFormat = null;
            });
        });

        var runnerLogger = loggerFactory.CreateLogger<DnxLiteRunner>();
        var cliLogger = loggerFactory.CreateLogger("fcdnx");

        try
        {
            var rawPackage = parseResult.GetRequiredValue(packageArg);
            (var packageId, var inlineVersion) = SplitInlineVersion(rawPackage);

            var explicitVersionRaw = parseResult.GetValue(versionOpt) ?? inlineVersion;
            NuGetVersion? explicitVersion = null;
            if (!string.IsNullOrWhiteSpace(explicitVersionRaw))
            {
                if (!NuGetVersion.TryParse(explicitVersionRaw, out explicitVersion))
                {
                    cliLogger.LogError("Invalid version '{Version}'.", explicitVersionRaw);
                    return ExitCodes.UsageError;
                }
            }

            var policyRaw = parseResult.GetValue(policyOpt) ?? nameof(ToolVersionPolicy.AlwaysLatest);
            if (!Enum.TryParse(policyRaw, ignoreCase: true, out ToolVersionPolicy policy))
            {
                cliLogger.LogError("Invalid --policy value '{Policy}'.", policyRaw);
                return ExitCodes.UsageError;
            }

            if (!TryBuildAdditionalEnvironment(
                parseResult.GetValue(envOpt) ?? Array.Empty<string>(),
                parseResult.GetValue(unsetEnvOpt) ?? Array.Empty<string>(),
                out var additionalEnvironment,
                out var envError))
            {
                cliLogger.LogError("{Error}", envError);
                return ExitCodes.UsageError;
            }

            var inheritEnvironmentVariables = !parseResult.GetValue(noInheritEnvOpt);
            var launchEnvironment = BuildLaunchEnvironment(inheritEnvironmentVariables, additionalEnvironment);

            var interactive = parseResult.GetValue(interactiveOpt);
            CredentialProviderSetup.Register(interactive, cliLogger);

            DotnetEnvironment environment;
            try
            {
                environment = await DotnetEnvironment.DetectAsync(ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                cliLogger.LogError(ex, "Failed to detect host .NET environment.");
                return ExitCodes.InternalError;
            }

            ToolCacheIndexStore indexStore = new();
            NuGetPackageResolverOptions resolverOptions = new()
            {
                RestrictToSource = parseResult.GetValue(sourceOpt),
                AdditionalSources = parseResult.GetValue(addSourceOpt) ?? Array.Empty<string>(),
                NuGetConfigFile = parseResult.GetValue(configFileOpt),
                IgnoreFailedSources = parseResult.GetValue(ignoreFailedSourcesOpt),
            };
            NuGetPackageResolver resolver = new(resolverOptions, indexStore, loggerFactory.CreateLogger<NuGetPackageResolver>());
            NuGetToolExtractor extractor = new(
                environment,
                parseResult.GetValue(configFileOpt),
                loggerFactory.CreateLogger<NuGetToolExtractor>());
            FieldCure.ToolHost.Execution.ToolLauncher launcher = new(loggerFactory.CreateLogger<FieldCure.ToolHost.Execution.ToolLauncher>());

            DnxLiteRunner runner = new(environment, resolver, extractor, launcher, runnerLogger);

            ToolInvocationRequest invocation = new()
            {
                PackageId = packageId,
                ToolArguments = parseResult.UnmatchedTokens.ToList(),
                Policy = policy,
                ExplicitVersion = explicitVersion,
                AllowPrerelease = parseResult.GetValue(prereleaseOpt),
                AllowRollForward = parseResult.GetValue(allowRollForwardOpt),
                InheritEnvironmentVariables = inheritEnvironmentVariables,
                AdditionalEnvironment = launchEnvironment,
            };

            using var process = await runner.StartAsync(invocation, ct).ConfigureAwait(false);
            return await ForwardStdioAsync(process, ct).ConfigureAwait(false);
        }
        catch (PackageNotFoundException ex)
        {
            cliLogger.LogError(ex, "Package not found.");
            return ExitCodes.PackageNotFound;
        }
        catch (NoCompatibleToolException ex)
        {
            cliLogger.LogError(ex, "Package has no compatible tools folder.");
            return ExitCodes.NoCompatibleTool;
        }
        catch (ExtractionFailedException ex)
        {
            cliLogger.LogError(ex, "Extraction failed.");
            return ExitCodes.ExtractionFailed;
        }
        catch (FieldCure.ToolHost.Execution.ToolLaunchFailedException ex)
        {
            cliLogger.LogError(ex, "Failed to launch tool.");
            return ExitCodes.LaunchFailed;
        }
        catch (FatalProtocolException ex)
        {
            cliLogger.LogError(ex, "NuGet protocol error.");
            return ExitCodes.NetworkOrAuthFailure;
        }
        catch (OperationCanceledException)
        {
            cliLogger.LogWarning("Operation canceled.");
            return ExitCodes.InternalError;
        }
        catch (Exception ex)
        {
            cliLogger.LogError(ex, "Unhandled error.");
            return ExitCodes.InternalError;
        }
    }

    /// <summary>Splits a <c>packageId@version</c> token into its components; returns <c>(raw, null)</c> when no <c>@</c> is present.</summary>
    internal static (string PackageId, string? Version) SplitInlineVersion(string raw)
    {
        var at = raw.IndexOf('@', StringComparison.Ordinal);
        return at < 0
            ? (raw, null)
            : (raw[..at], raw[(at + 1)..]);
    }

    /// <summary>Builds child-process environment overrides from CLI tokens.</summary>
    internal static bool TryBuildAdditionalEnvironment(
        IReadOnlyList<string> envTokens,
        IReadOnlyList<string> unsetTokens,
        out IReadOnlyDictionary<string, string?>? additionalEnvironment,
        out string? error)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, string?>? values = null;

        foreach (var token in envTokens)
        {
            var equals = token.IndexOf('=');
            if (equals <= 0)
            {
                additionalEnvironment = null;
                error = $"Invalid --env value '{token}'. Expected KEY=VALUE.";
                return false;
            }

            var key = token[..equals];
            if (string.IsNullOrWhiteSpace(key))
            {
                additionalEnvironment = null;
                error = "Invalid --env value. Environment variable name cannot be empty.";
                return false;
            }

            values ??= new Dictionary<string, string?>(comparer);
            values[key] = token[(equals + 1)..];
        }

        foreach (var key in unsetTokens)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                additionalEnvironment = null;
                error = "Invalid --unset-env value. Environment variable name cannot be empty.";
                return false;
            }

            values ??= new Dictionary<string, string?>(comparer);
            values[key] = null;
        }

        additionalEnvironment = values;
        error = null;
        return true;
    }

    private static IReadOnlyDictionary<string, string?>? BuildLaunchEnvironment(
        bool inheritEnvironmentVariables,
        IReadOnlyDictionary<string, string?>? additionalEnvironment)
    {
        if (inheritEnvironmentVariables)
        {
            return additionalEnvironment;
        }

        var launchEnvironment = ToolEnvironment.GetDefaultEnvironmentVariables();
        if (additionalEnvironment is { Count: > 0 })
        {
            foreach (var kvp in additionalEnvironment)
            {
                launchEnvironment[kvp.Key] = kvp.Value;
            }
        }

        return launchEnvironment;
    }

    /// <summary>
    /// Bidirectionally bridges this process's stdio to the child, then waits for exit.
    /// Returns the child's exit code.
    /// </summary>
    /// <remarks>
    /// Uses raw byte-stream copies (not line-based) so the bytes are forwarded verbatim — essential
    /// for long-lived stdio servers like MCP, whose JSON-RPC framing must not be re-encoded or have
    /// newlines normalized. Crucially, our stdin is pumped into the child's stdin for the lifetime of
    /// the connection (an MCP host streams requests this way); the child's stdin is only closed once
    /// OUR stdin reaches EOF (the host disconnected), which is the correct end-of-input signal. The
    /// previous implementation closed the child's stdin immediately, which made stdio servers see EOF
    /// at startup and shut down right after connecting.
    /// </remarks>
    private static async Task<int> ForwardStdioAsync(Process process, CancellationToken ct)
    {
        // Child output → our stdout/stderr. Our stdout is the JSON-RPC channel for an MCP host, so it
        // must carry only the child's stdout bytes (fcdnx's own logging goes to stderr — see logger setup).
        var pumpOut = PumpAsync(process.StandardOutput.BaseStream, Console.OpenStandardOutput(), ct);
        var pumpErr = PumpAsync(process.StandardError.BaseStream, Console.OpenStandardError(), ct);

        // Our stdin → child stdin, closing the child's stdin only when our stdin ends. Runs in the
        // background: an MCP host keeps stdin open for the whole session, so we must not await this
        // before the process exits or we would hang forever.
        var pumpIn = PumpStdinAsync(Console.OpenStandardInput(), process.StandardInput.BaseStream, ct);
        _ = pumpIn; // fire-and-forget; abandoned when the process exits

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        // Drain any final child output, but never block on stdin.
        try
        {
            await Task.WhenAll(pumpOut, pumpErr).WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best effort — don't hang shutdown on a slow drain.
        }
        catch (OperationCanceledException)
        {
            // Shutting down — ignore.
        }

        return process.ExitCode;
    }

    /// <summary>Copies <paramref name="from"/> to <paramref name="to"/> verbatim, flushing each chunk, until EOF.</summary>
    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await to.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — ignore.
        }
        catch (IOException)
        {
            // Peer closed the pipe — ignore.
        }
    }

    /// <summary>Pumps our stdin into the child's stdin, then closes the child's stdin on EOF.</summary>
    private static async Task PumpStdinAsync(Stream from, Stream childStdin, CancellationToken ct)
    {
        try
        {
            await PumpAsync(from, childStdin, ct).ConfigureAwait(false);
        }
        finally
        {
            // Our stdin ended (host disconnected) — signal end-of-input to the child.
            try
            {
                childStdin.Close();
            }
            catch (IOException)
            {
                // Already closed — ignore.
            }
        }
    }
}
