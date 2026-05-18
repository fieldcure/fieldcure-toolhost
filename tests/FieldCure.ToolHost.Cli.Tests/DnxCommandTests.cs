using System.CommandLine;
using FieldCure.ToolHost.Cli.Output;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace FieldCure.ToolHost.Cli.Tests;

public sealed class DnxCommandTests
{
    [Xunit.Theory]
    [Xunit.InlineData("dotnetsay", "dotnetsay", null)]
    [Xunit.InlineData("dotnetsay@2.1.0", "dotnetsay", "2.1.0")]
    [Xunit.InlineData("MyCorp.Tool@1.0.0-beta.1", "MyCorp.Tool", "1.0.0-beta.1")]
    public void SplitInlineVersion_ExtractsVersion(string raw, string expectedId, string? expectedVersion)
    {
        (string id, string? version) = DnxCommand.SplitInlineVersion(raw);
        _ = id.Should().Be(expectedId);
        _ = version.Should().Be(expectedVersion);
    }

    [Xunit.Fact]
    public void Build_ProducesParseableHelp()
    {
        RootCommand root = DnxCommand.Build();

        ParseResult parseResult = root.Parse(new[] { "--help" });

        _ = parseResult.Errors.Should().BeEmpty();
    }

    [Xunit.Fact]
    public void Build_ParsesPackageArg()
    {
        RootCommand root = DnxCommand.Build();
        ParseResult parseResult = root.Parse(new[] { "dotnetsay" });
        _ = parseResult.Errors.Should().BeEmpty();
    }
}

public sealed class VerbosityMapperTests
{
    [Xunit.Theory]
    [Xunit.InlineData("quiet", LogLevel.None)]
    [Xunit.InlineData("q", LogLevel.None)]
    [Xunit.InlineData("minimal", LogLevel.Warning)]
    [Xunit.InlineData("m", LogLevel.Warning)]
    [Xunit.InlineData("normal", LogLevel.Information)]
    [Xunit.InlineData("n", LogLevel.Information)]
    [Xunit.InlineData("detailed", LogLevel.Debug)]
    [Xunit.InlineData("d", LogLevel.Debug)]
    [Xunit.InlineData("diagnostic", LogLevel.Trace)]
    [Xunit.InlineData("diag", LogLevel.Trace)]
    public void Map_ReturnsExpectedLevel(string verbosity, LogLevel expected)
    {
        _ = VerbosityMapper.Map(verbosity).Should().Be(expected);
    }

    [Xunit.Fact]
    public void Map_NullOrUnknown_DefaultsToInformation()
    {
        _ = VerbosityMapper.Map(null).Should().Be(LogLevel.Information);
        _ = VerbosityMapper.Map("nonsense").Should().Be(LogLevel.Information);
    }
}
