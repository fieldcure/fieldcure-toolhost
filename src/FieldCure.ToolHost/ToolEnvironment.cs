using System.Runtime.InteropServices;

namespace FieldCure.ToolHost;

/// <summary>
/// Helpers for constructing child-process environments for launched tools.
/// </summary>
public static class ToolEnvironment
{
    private static readonly string[] s_defaultWindowsVars =
    [
        "APPDATA", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA", "PATH", "PATHEXT",
        "PROCESSOR_ARCHITECTURE", "PROGRAMFILES", "SYSTEMDRIVE", "SYSTEMROOT",
        "TEMP", "TMP", "USERNAME", "USERPROFILE", "WINDIR",
    ];

    private static readonly string[] s_defaultUnixVars =
    [
        "HOME", "LOGNAME", "PATH", "SHELL", "TERM", "TMPDIR", "USER",
    ];

    /// <summary>
    /// Returns a curated subset of the current process environment that most tools need to start.
    /// </summary>
    /// <returns>
    /// A new dictionary containing platform-standard variables such as <c>PATH</c>, home-directory
    /// variables, and system temporary-directory variables. Variables whose values look like shell
    /// function definitions are skipped.
    /// </returns>
    /// <remarks>
    /// Use this helper with <see cref="ToolInvocationRequest.InheritEnvironmentVariables"/> set to
    /// <see langword="false"/> when a host wants to avoid exposing unrelated credentials or process
    /// settings to the launched tool while still providing enough environment for normal startup.
    /// Add tool-specific secrets through <see cref="ToolInvocationRequest.AdditionalEnvironment"/>.
    /// </remarks>
    public static Dictionary<string, string?> GetDefaultEnvironmentVariables()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var names = isWindows ? s_defaultWindowsVars : s_defaultUnixVars;
        var result = new Dictionary<string, string?>(
            isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null || value.StartsWith("()", StringComparison.Ordinal))
            {
                continue;
            }

            result[name] = value;
        }

        return result;
    }
}
