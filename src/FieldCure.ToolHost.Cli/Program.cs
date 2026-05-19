namespace FieldCure.ToolHost.Cli;

/// <summary>Entry point for the <c>fcdnx</c> CLI.</summary>
public static class Program
{
    /// <summary>CLI main. Returns a process exit code (see <see cref="ExitCodes"/>).</summary>
    /// <param name="args">Raw command-line arguments.</param>
    public static Task<int> Main(string[] args)
    {
        var root = DnxCommand.Build();
        var parseResult = root.Parse(args);
        return parseResult.InvokeAsync();
    }
}
