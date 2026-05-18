namespace FieldCure.ToolHost;

/// <summary>
/// Resolves on-disk locations used by ToolHost for its metadata, logs, and temporary files.
/// </summary>
/// <remarks>
/// <para>
/// The metadata root is per-user, non-roaming local application data:
/// </para>
/// <list type="bullet">
///   <item><description>Windows: <c>%LOCALAPPDATA%\FieldCure\ToolHost\</c></description></item>
///   <item><description>Linux: <c>$XDG_DATA_HOME/FieldCure/ToolHost/</c> (default <c>~/.local/share/FieldCure/ToolHost/</c>)</description></item>
///   <item><description>macOS: <c>~/Library/Application Support/FieldCure/ToolHost/</c></description></item>
/// </list>
/// </remarks>
public static class ToolHostPaths
{
    private const string CompanyFolder = "FieldCure";
    private const string ProductFolder = "ToolHost";

    /// <summary>Returns the metadata root folder. Creates it on first access.</summary>
    public static string GetMetadataRoot()
    {
        var baseFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        var root = Path.Combine(baseFolder, CompanyFolder, ProductFolder);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Returns the path to the persisted cache index file (<c>_index.json</c>).</summary>
    public static string GetIndexFilePath() => Path.Combine(GetMetadataRoot(), "_index.json");

    /// <summary>Returns the path to the logs subfolder. Creates it on first access.</summary>
    public static string GetLogsFolder()
    {
        var folder = Path.Combine(GetMetadataRoot(), "logs");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Returns the path to the temporary downloads subfolder. Creates it on first access.</summary>
    public static string GetTempFolder()
    {
        var folder = Path.Combine(GetMetadataRoot(), "tmp");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
