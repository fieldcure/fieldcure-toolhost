namespace FieldCure.ToolHost.Cli.Tests;

public sealed class PlaceholderTests
{
    [Xunit.Fact]
    public void Sanity()
    {
        FluentAssertions.AssertionExtensions.Should((1 + 1)).Be(2);
    }
}
