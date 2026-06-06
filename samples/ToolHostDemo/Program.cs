namespace FieldCure.ToolHost.Samples.Demo;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var packageId = args.Length > 0 ? args[0] : "dotnetsay";
        IReadOnlyList<string> toolArgs = args.Length > 1 ? args[1..] : Array.Empty<string>();

        Console.Out.WriteLine($"[ToolHostDemo] Running '{packageId}' via DnxLiteRunner...");

        var environment = await DotnetEnvironment.DetectAsync();
        DnxLiteRunner runner = new(environment);

        ToolInvocationRequest request = new()
        {
            PackageId = packageId,
            ToolArguments = toolArgs,
            Policy = ToolVersionPolicy.CachedWithRefresh,
        };

        using var process = await runner.StartAsync(request);

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
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
