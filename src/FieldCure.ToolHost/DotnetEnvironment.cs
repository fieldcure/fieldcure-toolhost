using System.Diagnostics;
using System.Runtime.InteropServices;

using NuGet.Configuration;

namespace FieldCure.ToolHost;

/// <summary>
/// Captures the host .NET environment for diagnostics and warm-cache decisions.
/// Computed once at startup and cached for the process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// Detection of the installed SDK is used <b>only</b> for diagnostics and warm-cache optimization,
/// never for delegating execution to <c>dnx</c> itself. <see cref="DnxLiteRunner"/> always uses
/// the DnxLite execution path regardless of <see cref="HasSdk10OrLater"/>.
/// </para>
/// </remarks>
public sealed record DotnetEnvironment
{
    /// <summary>Installed SDK versions (e.g., <c>["10.0.100", "9.0.300"]</c>). Empty when SDK is absent.</summary>
    public required IReadOnlyList<string> InstalledSdks { get; init; }

    /// <summary>Installed shared runtime versions for <c>Microsoft.NETCore.App</c>.</summary>
    public required IReadOnlyList<string> InstalledRuntimes { get; init; }

    /// <summary>Absolute path to the <c>dotnet</c> muxer used by DnxLite for <c>dotnet exec</c>.</summary>
    public required string DotnetMuxerPath { get; init; }

    /// <summary>Effective NuGet global packages folder (honors <c>NUGET_PACKAGES</c> env var and <c>NuGet.Config</c>).</summary>
    public required string NuGetGlobalPackagesFolder { get; init; }

    /// <summary>Current process runtime identifier (e.g., <c>"win-x64"</c>, <c>"linux-arm64"</c>).</summary>
    public required string RuntimeIdentifier { get; init; }

    /// <summary>True iff at least one installed SDK has a major version &gt;= 10.</summary>
    public bool HasSdk10OrLater { get; init; }

    /// <summary>
    /// Detects the host environment. Runs <c>dotnet --list-sdks</c> and <c>dotnet --list-runtimes</c>
    /// as subprocesses. Throws <see cref="InvalidOperationException"/> if the dotnet muxer
    /// is not on <c>PATH</c> (and <c>DOTNET_ROOT</c> is unset) or returns a non-zero exit code.
    /// </summary>
    /// <param name="ct">Cancellation token honored during subprocess waits.</param>
    public static async Task<DotnetEnvironment> DetectAsync(CancellationToken ct = default)
    {
        var muxer = LocateDotnetMuxer()
            ?? throw new InvalidOperationException(
                "The 'dotnet' muxer was not found on PATH and DOTNET_ROOT is not set. " +
                "Install the .NET runtime or set DOTNET_ROOT to its installation folder.");

        var sdks = ParseSdkLines(await RunDotnetAsync(muxer, "--list-sdks", ct).ConfigureAwait(false));
        var runtimes = ParseRuntimeLines(await RunDotnetAsync(muxer, "--list-runtimes", ct).ConfigureAwait(false));

        var settings = Settings.LoadDefaultSettings(root: null);
        var globalPackages = SettingsUtility.GetGlobalPackagesFolder(settings);

        return new DotnetEnvironment
        {
            InstalledSdks = sdks,
            InstalledRuntimes = runtimes,
            DotnetMuxerPath = muxer,
            NuGetGlobalPackagesFolder = globalPackages,
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            HasSdk10OrLater = sdks.Any(v => TryParseMajor(v, out var major) && major >= 10),
        };
    }

    internal static string? LocateDotnetMuxer()
    {
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            var candidate = Path.Combine(root, exe);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> ParseSdkLines(string output)
    {
        // Each line: "<version> [<install-path>]"
        List<string> versions = [];
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var bracket = trimmed.IndexOf('[');
            var version = (bracket > 0 ? trimmed[..bracket] : trimmed).Trim();
            if (version.Length > 0)
            {
                versions.Add(version);
            }
        }
        return versions;
    }

    internal static IReadOnlyList<string> ParseRuntimeLines(string output)
    {
        // Each line: "<framework> <version> [<install-path>]"
        // We only retain Microsoft.NETCore.App runtimes.
        List<string> versions = [];
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[0], "Microsoft.NETCore.App", StringComparison.Ordinal))
            {
                versions.Add(parts[1]);
            }
        }
        return versions;
    }

    private static bool TryParseMajor(string version, out int major)
    {
        var dot = version.IndexOf('.');
        var head = dot > 0 ? version[..dot] : version;
        return int.TryParse(head, out major);
    }

    private static async Task<string> RunDotnetAsync(string muxer, string arg, CancellationToken ct)
    {
        ProcessStartInfo psi = new()
        {
            FileName = muxer,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{muxer} {arg}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{muxer} {arg}' exited with code {process.ExitCode}. stderr: {stderr.Trim()}");
        }

        return stdout;
    }
}
