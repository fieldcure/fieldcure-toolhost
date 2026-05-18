using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Resolution;

using Microsoft.Extensions.Logging;

using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

using INuGetLogger = NuGet.Common.ILogger;
using NuGetLogMessage = NuGet.Common.ILogMessage;
using NuGetLoggerBase = NuGet.Common.LoggerBase;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace FieldCure.ToolHost.Extraction;

/// <summary>
/// Default <see cref="IToolExtractor"/> implementation backed by <c>NuGet.Packaging</c>.
/// Reuses the NuGet global packages folder so installs interop with <c>dotnet nuget locals</c>.
/// </summary>
public sealed class NuGetToolExtractor : IToolExtractor
{
    private static readonly NuGetFramework HostFramework = NuGetFramework.Parse("net10.0");

    private readonly DotnetEnvironment _environment;
    private readonly ILogger<NuGetToolExtractor> _logger;
    private readonly INuGetLogger _nugetLogger;

    /// <summary>Constructs an extractor bound to the supplied host environment.</summary>
    /// <param name="environment">Detected host environment, used for the NuGet global folder and host RID.</param>
    /// <param name="logger">Optional logger.</param>
    public NuGetToolExtractor(DotnetEnvironment environment, ILogger<NuGetToolExtractor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NuGetToolExtractor>.Instance;
        _nugetLogger = new ForwardingNuGetLogger(_logger);
    }

    /// <inheritdoc />
    public async Task<ExtractedToolLayout> EnsureExtractedAsync(PackageResolution resolution, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var globalFolder = _environment.NuGetGlobalPackagesFolder;
        VersionFolderPathResolver pathResolver = new(globalFolder);
        var packagePath = pathResolver.GetInstallPath(resolution.PackageId, resolution.Version);
        var metadataMarker = Path.Combine(packagePath, ".nupkg.metadata");

        if (!File.Exists(metadataMarker))
        {
            await DownloadAndInstallAsync(resolution, globalFolder, ct).ConfigureAwait(false);
        }

        if (!File.Exists(metadataMarker))
        {
            throw new ExtractionFailedException(
                $"Package '{resolution.PackageId}' {resolution.Version.ToNormalizedString()} was not present after extraction.");
        }

        (var toolsFolder, var tfm, var rid) = SelectToolsFolder(resolution.PackageId, packagePath);

        var settingsXmlPath = Path.Combine(toolsFolder, "DotnetToolSettings.xml");
        if (!File.Exists(settingsXmlPath))
        {
            throw new NoCompatibleToolException(resolution.PackageId,
                $"Package '{resolution.PackageId}' is missing DotnetToolSettings.xml at '{settingsXmlPath}'.");
        }
        DotnetToolSettings toolSettings = DotnetToolSettings.ParseFile(settingsXmlPath);

        return new ExtractedToolLayout(packagePath, toolsFolder, tfm, rid, toolSettings);
    }

    private async Task DownloadAndInstallAsync(PackageResolution resolution, string globalFolder, CancellationToken ct)
    {
        PackageIdentity identity = new(resolution.PackageId, resolution.Version);
        var downloadFolder = ToolHostPaths.GetTempFolder();

        using SourceCacheContext cacheContext = new();
        PackageDownloadContext downloadContext = new(cacheContext, downloadFolder, directDownload: false);

        ISettings settings = Settings.LoadDefaultSettings(root: null);
        List<SourceRepository> sources = BuildSources(settings, resolution.SourceUrl);

        List<Exception> failures = new();
        foreach (SourceRepository repository in sources)
        {
            try
            {
                DownloadResource downloadResource = await repository.GetResourceAsync<DownloadResource>(ct).ConfigureAwait(false);
                using DownloadResourceResult result = await downloadResource
                    .GetDownloadResourceResultAsync(identity, downloadContext, globalFolder, _nugetLogger, ct)
                    .ConfigureAwait(false);

                if (result.Status == DownloadResourceResultStatus.Available
                    || result.Status == DownloadResourceResultStatus.AvailableWithoutStream)
                {
                    return;
                }

                failures.Add(new ExtractionFailedException(
                    $"Source '{repository.PackageSource.Source}' returned status '{result.Status}'."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        var detail = failures.Count == 0
            ? "No NuGet sources are configured."
            : string.Join("; ", failures.Select(f => f.Message));
        throw new ExtractionFailedException(
            $"Failed to install package '{resolution.PackageId}' {resolution.Version.ToNormalizedString()}: {detail}");
    }

    private static List<SourceRepository> BuildSources(ISettings settings, string preferredSourceUrl)
    {
        PackageSourceProvider provider = new(settings);
        List<PackageSource> enabled = provider.LoadPackageSources().Where(s => s.IsEnabled).ToList();

        // Try the preferred source first if it is among the configured sources.
        PackageSource? preferred = enabled.FirstOrDefault(s =>
            string.Equals(s.Source, preferredSourceUrl, StringComparison.OrdinalIgnoreCase));

        List<SourceRepository> ordered = new();
        if (preferred is not null)
        {
            ordered.Add(Repository.Factory.GetCoreV3(preferred));
            foreach (PackageSource other in enabled.Where(s => s != preferred))
            {
                ordered.Add(Repository.Factory.GetCoreV3(other));
            }
        }
        else
        {
            ordered.Add(Repository.Factory.GetCoreV3(new PackageSource("ToolHost-resolved", preferredSourceUrl)));
            foreach (PackageSource other in enabled)
            {
                ordered.Add(Repository.Factory.GetCoreV3(other));
            }
        }
        return ordered;
    }

    private (string ToolsFolder, string TargetFramework, string RuntimeIdentifier) SelectToolsFolder(string packageId, string packagePath)
    {
        var toolsRoot = Path.Combine(packagePath, "tools");
        if (!Directory.Exists(toolsRoot))
        {
            throw new NoCompatibleToolException(packageId,
                $"Package '{packageId}' has no tools/ folder at '{toolsRoot}'. It is not a .NET tool package.");
        }

        List<(NuGetFramework Framework, string RuntimeIdentifier, string Path)> candidates = new();
        foreach (var tfmFolder in Directory.EnumerateDirectories(toolsRoot))
        {
            NuGetFramework framework;
            try
            {
                framework = NuGetFramework.ParseFolder(Path.GetFileName(tfmFolder));
            }
            catch (Exception)
            {
                continue;
            }
            if (framework.IsUnsupported)
            {
                continue;
            }

            foreach (var ridFolder in Directory.EnumerateDirectories(tfmFolder))
            {
                var rid = Path.GetFileName(ridFolder);
                candidates.Add((framework, rid, ridFolder));
            }
        }

        if (candidates.Count == 0)
        {
            throw new NoCompatibleToolException(packageId,
                $"Package '{packageId}' has no usable tools/{{tfm}}/{{rid}} folders.");
        }

        FrameworkReducer reducer = new();
        IEnumerable<NuGetFramework> distinctFrameworks = candidates.Select(c => c.Framework).Distinct();
        NuGetFramework? bestFramework = reducer.GetNearest(HostFramework, distinctFrameworks);
        if (bestFramework is null)
        {
            throw new NoCompatibleToolException(packageId,
                $"Package '{packageId}' has no tools folder compatible with the host framework {HostFramework.DotNetFrameworkName}.");
        }

        IEnumerable<(NuGetFramework Framework, string RuntimeIdentifier, string Path)> frameworkMatches = candidates
            .Where(c => c.Framework.Equals(bestFramework));

        (NuGetFramework Framework, string RuntimeIdentifier, string Path) anyMatch = frameworkMatches
            .FirstOrDefault(c => string.Equals(c.RuntimeIdentifier, "any", StringComparison.OrdinalIgnoreCase));

        (NuGetFramework Framework, string RuntimeIdentifier, string Path) hostMatch = frameworkMatches
            .FirstOrDefault(c => string.Equals(c.RuntimeIdentifier, _environment.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));

        (NuGetFramework Framework, string RuntimeIdentifier, string Path) chosen;
        if (hostMatch.Path is not null)
        {
            chosen = hostMatch;
        }
        else if (anyMatch.Path is not null)
        {
            chosen = anyMatch;
        }
        else
        {
            // v0.1 best-effort: pick any present RID; full RID-graph traversal lands in v0.2.
            chosen = frameworkMatches.First();
        }

        return (chosen.Path, chosen.Framework.GetShortFolderName(), chosen.RuntimeIdentifier);
    }

    private sealed class ForwardingNuGetLogger : NuGetLoggerBase
    {
        private readonly ILogger _inner;
        public ForwardingNuGetLogger(ILogger inner) => _inner = inner;

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
