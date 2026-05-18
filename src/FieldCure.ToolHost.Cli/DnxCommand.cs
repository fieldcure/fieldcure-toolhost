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
            verbosityOpt,
            frameworkOpt,
            archOpt,
            osOpt,
            policyOpt,
        };

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
        Option<string?> verbosityOpt,
        Option<string?> policyOpt,
        CancellationToken ct)
    {
        var verbosity = parseResult.GetValue(verbosityOpt) ?? VerbosityMapper.DefaultLevel;
        LogLevel minLevel = VerbosityMapper.Map(verbosity);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            _ = b.SetMinimumLevel(minLevel);
            _ = b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.IncludeScopes = false;
                o.TimestampFormat = null;
            });
        });

        ILogger<DnxLiteRunner> runnerLogger = loggerFactory.CreateLogger<DnxLiteRunner>();
        ILogger cliLogger = loggerFactory.CreateLogger("fcdnx");

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
            NuGetToolExtractor extractor = new(environment, loggerFactory.CreateLogger<NuGetToolExtractor>());
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
            };

            using Process process = await runner.StartAsync(invocation, ct).ConfigureAwait(false);
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

    /// <summary>Forwards child stdout/stderr to the parent console line-by-line, closes the child's stdin to signal EOF, and waits for exit. Returns the child's exit code.</summary>
    private static async Task<int> ForwardStdioAsync(Process process, CancellationToken ct)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Out.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Signal EOF on the child's stdin so tools that read it (rare) don't hang.
        // Future enhancement: forward Console.In line-by-line.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Stream already closed — ignore.
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }
}
