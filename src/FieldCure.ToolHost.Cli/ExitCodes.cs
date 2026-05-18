namespace FieldCure.ToolHost.Cli;

/// <summary>
/// Process exit codes used by the <c>fcdnx</c> CLI. Values ≥64 follow <c>sysexits.h</c> convention.
/// </summary>
public static class ExitCodes
{
    /// <summary>Tool exited normally with code 0.</summary>
    public const int Success = 0;

    /// <summary>Usage error (bad flags).</summary>
    public const int UsageError = 64;

    /// <summary>Requested NuGet package was not found.</summary>
    public const int PackageNotFound = 65;

    /// <summary>Package was found but has no compatible <c>tools/{tfm}/{rid}</c> folder.</summary>
    public const int NoCompatibleTool = 66;

    /// <summary>Network or authentication error during resolution or download.</summary>
    public const int NetworkOrAuthFailure = 67;

    /// <summary>Extraction failed (corrupt package, disk full, etc.).</summary>
    public const int ExtractionFailed = 68;

    /// <summary>Tool process failed to start.</summary>
    public const int LaunchFailed = 69;

    /// <summary>Internal/unhandled error in ToolHost itself.</summary>
    public const int InternalError = 70;
}
