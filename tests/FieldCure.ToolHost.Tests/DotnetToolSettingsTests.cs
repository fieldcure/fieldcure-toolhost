using FieldCure.ToolHost.Execution;

using FluentAssertions;

namespace FieldCure.ToolHost.Tests;

public sealed class DotnetToolSettingsTests
{
    [Xunit.Fact]
    public void Parse_ManagedToolSingleCommand_ReturnsDotnetRunner()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="dotnetsay" EntryPoint="dotnetsay.dll" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """;

        DotnetToolSettings settings = DotnetToolSettings.Parse(xml);

        _ = settings.Commands.Should().HaveCount(1);
        var command = settings.Commands[0];
        _ = command.Name.Should().Be("dotnetsay");
        _ = command.EntryPoint.Should().Be("dotnetsay.dll");
        _ = command.Runner.Should().Be("dotnet");
        _ = command.UsesDotnetRunner.Should().BeTrue();
    }

    [Xunit.Fact]
    public void Parse_SelfContainedTool_FlagsExecutableRunner()
    {
        const string xml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="aotsay" EntryPoint="aotsay.exe" Runner="executable" />
              </Commands>
            </DotNetCliTool>
            """;

        var command = DotnetToolSettings.Parse(xml).Commands[0];

        _ = command.UsesDotnetRunner.Should().BeFalse();
        _ = command.Runner.Should().Be("executable");
    }

    [Xunit.Fact]
    public void Parse_MultipleCommands_ReturnsAll()
    {
        const string xml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="cmd1" EntryPoint="a.dll" Runner="dotnet" />
                <Command Name="cmd2" EntryPoint="b.dll" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """;

        DotnetToolSettings settings = DotnetToolSettings.Parse(xml);

        _ = settings.Commands.Should().HaveCount(2);
        _ = settings.Commands.Select(c => c.Name).Should().Equal("cmd1", "cmd2");
    }

    [Xunit.Fact]
    public void Parse_MissingRoot_Throws()
    {
        var xml = "<NotARoot/>";
        System.Action act = () => DotnetToolSettings.Parse(xml);
        _ = act.Should().Throw<InvalidDataException>().WithMessage("*DotNetCliTool*");
    }

    [Xunit.Fact]
    public void Parse_NoCommandsElement_Throws()
    {
        var xml = "<DotNetCliTool Version=\"1\"></DotNetCliTool>";
        System.Action act = () => DotnetToolSettings.Parse(xml);
        _ = act.Should().Throw<InvalidDataException>().WithMessage("*Commands*");
    }

    [Xunit.Fact]
    public void Parse_NoCommandEntries_Throws()
    {
        var xml = "<DotNetCliTool Version=\"1\"><Commands></Commands></DotNetCliTool>";
        System.Action act = () => DotnetToolSettings.Parse(xml);
        _ = act.Should().Throw<InvalidDataException>().WithMessage("*no*Command*");
    }

    [Xunit.Fact]
    public void Parse_MissingAttribute_Throws()
    {
        var xml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="x" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """;
        System.Action act = () => DotnetToolSettings.Parse(xml);
        _ = act.Should().Throw<InvalidDataException>().WithMessage("*EntryPoint*");
    }

    [Xunit.Fact]
    public void Parse_Malformed_Throws()
    {
        var xml = "<DotNetCliTool";
        System.Action act = () => DotnetToolSettings.Parse(xml);
        _ = act.Should().Throw<InvalidDataException>();
    }
}
