using FluentAssertions;

namespace FieldCure.ToolHost.Tests;

public sealed class DotnetEnvironmentParserTests
{
    [Xunit.Fact]
    public void ParseSdkLines_StandardOutput_ReturnsVersionsOnly()
    {
        const string output = """
            8.0.416 [C:\Program Files\dotnet\sdk]
            9.0.308 [C:\Program Files\dotnet\sdk]
            10.0.300 [C:\Program Files\dotnet\sdk]
            """;

        var sdks = DotnetEnvironment.ParseSdkLines(output);

        _ = sdks.Should().Equal("8.0.416", "9.0.308", "10.0.300");
    }

    [Xunit.Fact]
    public void ParseSdkLines_BlankAndWhitespaceLines_AreIgnored()
    {
        const string output = "\n  \n10.0.300 [C:\\Program Files\\dotnet\\sdk]\n\n";

        var sdks = DotnetEnvironment.ParseSdkLines(output);

        _ = sdks.Should().Equal("10.0.300");
    }

    [Xunit.Fact]
    public void ParseSdkLines_NoInstallPath_StillReturnsVersion()
    {
        const string output = "10.0.300";
        var sdks = DotnetEnvironment.ParseSdkLines(output);
        _ = sdks.Should().Equal("10.0.300");
    }

    [Xunit.Fact]
    public void ParseRuntimeLines_FiltersToNETCoreApp()
    {
        const string output = """
            Microsoft.AspNetCore.App 10.0.6 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
            Microsoft.NETCore.App 10.0.6 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            Microsoft.WindowsDesktop.App 10.0.6 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
            Microsoft.NETCore.App 9.0.16 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
            """;

        var runtimes = DotnetEnvironment.ParseRuntimeLines(output);

        _ = runtimes.Should().Equal("10.0.6", "9.0.16");
    }

    [Xunit.Fact]
    public void ParseRuntimeLines_MalformedLines_AreSkipped()
    {
        const string output = """
            garbage
            Microsoft.NETCore.App
            Microsoft.NETCore.App 10.0.6 [path]
            """;

        var runtimes = DotnetEnvironment.ParseRuntimeLines(output);

        _ = runtimes.Should().Equal("10.0.6");
    }
}
