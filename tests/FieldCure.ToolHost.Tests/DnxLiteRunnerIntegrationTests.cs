using System.Diagnostics;
using System.Text;

using FluentAssertions;

namespace FieldCure.ToolHost.Tests;

/// <summary>
/// End-to-end smoke test against the live nuget.org feed. Skipped unless RUN_INTEGRATION=1
/// to keep PR-time CI runs fast and network-independent.
/// </summary>
[Xunit.Trait("Category", "Integration")]
public sealed class DnxLiteRunnerIntegrationTests
{
    [Xunit.Fact]
    public async Task Resolves_Extracts_And_Launches_DotnetSay()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        DotnetEnvironment environment = await DotnetEnvironment.DetectAsync();
        DnxLiteRunner runner = new(environment);

        ToolInvocationRequest request = new()
        {
            PackageId = "dotnetsay",
            ToolArguments = Array.Empty<string>(),
            Policy = ToolVersionPolicy.AlwaysLatest,
        };

        using Process process = await runner.StartAsync(request);

        StringBuilder stdout = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _ = stdout.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        await process.WaitForExitAsync();

        _ = process.ExitCode.Should().Be(0);
        _ = stdout.ToString().Should().NotBeNullOrWhiteSpace();
    }
}
