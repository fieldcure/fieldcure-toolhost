using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace FieldCure.ToolHost.Execution;

/// <summary>
/// Default <see cref="IToolLauncher"/>. Builds a <see cref="ProcessStartInfo"/> per the rules in
/// the ToolHost specification (managed runner → <c>dotnet exec</c>; executable runner → direct invoke).
/// </summary>
public sealed class ToolLauncher : IToolLauncher
{
    private readonly ILogger<ToolLauncher> _logger;

    /// <summary>Constructs a new launcher.</summary>
    /// <param name="logger">Optional logger.</param>
    public ToolLauncher(ILogger<ToolLauncher>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ToolLauncher>.Instance;
    }

    /// <inheritdoc />
    public Process Start(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Layout);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DotnetMuxerPath);

        if (request.Layout.ToolSettings.Commands.Count == 0)
        {
            throw new ToolLaunchFailedException(
                $"Package has no commands declared in DotnetToolSettings.xml at '{request.Layout.ToolsFolder}'.");
        }

        DotnetToolCommand command = request.Layout.ToolSettings.Commands[0];
        var entryPoint = Path.Combine(request.Layout.ToolsFolder, command.EntryPoint);

        if (!File.Exists(entryPoint))
        {
            throw new ToolLaunchFailedException(
                $"Tool entry point '{entryPoint}' (from DotnetToolSettings.xml) does not exist.");
        }

        ProcessStartInfo psi = new()
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.Layout.ToolsFolder,
        };

        if (command.UsesDotnetRunner)
        {
            psi.FileName = request.DotnetMuxerPath;
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(entryPoint);
        }
        else
        {
            psi.FileName = entryPoint;
        }

        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (request.AllowRollForward)
        {
            psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";
        }

        if (request.AdditionalEnvironment is { Count: > 0 } extras)
        {
            foreach (KeyValuePair<string, string?> kv in extras)
            {
                if (kv.Value is null)
                {
                    _ = psi.Environment.Remove(kv.Key);
                }
                else
                {
                    psi.Environment[kv.Key] = kv.Value;
                }
            }
        }

        _logger.LogDebug("Launching tool: {FileName} {Args}", psi.FileName, string.Join(' ', psi.ArgumentList));

        try
        {
            Process? process = Process.Start(psi);
            if (process is null)
            {
                throw new ToolLaunchFailedException(
                    $"Process.Start returned null for '{psi.FileName}'.");
            }
            return process;
        }
        catch (Exception ex) when (ex is not ToolLaunchFailedException)
        {
            throw new ToolLaunchFailedException(
                $"Failed to start tool process '{psi.FileName}'.", ex);
        }
    }
}
