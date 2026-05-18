using System.Xml.Linq;

namespace FieldCure.ToolHost.Execution;

/// <summary>
/// Parsed contents of <c>DotnetToolSettings.xml</c> as it appears at the root of a
/// .NET tool package's <c>tools/{tfm}/{rid}/</c> directory.
/// </summary>
/// <remarks>
/// <para>
/// The schema is owned by Microsoft.NET.Sdk. Example contents:
/// </para>
/// <code language="xml">
/// &lt;DotNetCliTool Version="1"&gt;
///   &lt;Commands&gt;
///     &lt;Command Name="dotnetsay" EntryPoint="dotnetsay.dll" Runner="dotnet" /&gt;
///   &lt;/Commands&gt;
/// &lt;/DotNetCliTool&gt;
/// </code>
/// </remarks>
public sealed record DotnetToolSettings
{
    /// <summary>Commands declared by the tool. Most tools declare exactly one.</summary>
    public required IReadOnlyList<DotnetToolCommand> Commands { get; init; }

    /// <summary>
    /// Parses an in-memory XML document.
    /// </summary>
    /// <param name="xml">Raw XML contents.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="xml"/> is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the document is missing required elements/attributes.</exception>
    public static DotnetToolSettings Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException("DotnetToolSettings.xml is not well-formed XML.", ex);
        }

        XElement root = doc.Root
            ?? throw new InvalidDataException("DotnetToolSettings.xml has no root element.");

        if (!string.Equals(root.Name.LocalName, "DotNetCliTool", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"DotnetToolSettings.xml root must be <DotNetCliTool>, found <{root.Name.LocalName}>.");
        }

        XElement? commandsElement = root.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Commands", StringComparison.Ordinal));

        if (commandsElement is null)
        {
            throw new InvalidDataException("DotnetToolSettings.xml is missing the <Commands> element.");
        }

        List<DotnetToolCommand> commands = new();
        foreach (XElement commandElement in commandsElement.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "Command", StringComparison.Ordinal)))
        {
            var name = RequireAttribute(commandElement, "Name");
            var entryPoint = RequireAttribute(commandElement, "EntryPoint");
            var runner = RequireAttribute(commandElement, "Runner");

            commands.Add(new DotnetToolCommand
            {
                Name = name,
                EntryPoint = entryPoint,
                Runner = runner,
            });
        }

        if (commands.Count == 0)
        {
            throw new InvalidDataException("DotnetToolSettings.xml declares no <Command> entries.");
        }

        return new DotnetToolSettings { Commands = commands };
    }

    /// <summary>
    /// Reads and parses a <c>DotnetToolSettings.xml</c> file from disk.
    /// </summary>
    /// <param name="filePath">Absolute path to the XML file.</param>
    public static DotnetToolSettings ParseFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var xml = File.ReadAllText(filePath);
        return Parse(xml);
    }

    /// <summary>Returns the value of a required attribute, throwing <see cref="InvalidDataException"/> when missing or empty.</summary>
    private static string RequireAttribute(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException(
                $"<{element.Name.LocalName}> is missing the required '{attributeName}' attribute.");
        }
        return value;
    }
}

/// <summary>A single command entry declared inside <see cref="DotnetToolSettings"/>.</summary>
public sealed record DotnetToolCommand
{
    /// <summary>Command name as invoked on the command line (e.g., <c>"dotnetsay"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Entry point relative to the tool's <c>tools/{tfm}/{rid}/</c> directory
    /// (e.g., <c>"dotnetsay.dll"</c> for managed runners, or an executable name for self-contained tools).
    /// </summary>
    public required string EntryPoint { get; init; }

    /// <summary>
    /// Runner kind. Typically <c>"dotnet"</c> for managed tools or <c>"executable"</c>
    /// for self-contained / NativeAOT tools.
    /// </summary>
    public required string Runner { get; init; }

    /// <summary>True iff this command's <see cref="Runner"/> is <c>"dotnet"</c> (case-insensitive).</summary>
    public bool UsesDotnetRunner => string.Equals(Runner, "dotnet", StringComparison.OrdinalIgnoreCase);
}
