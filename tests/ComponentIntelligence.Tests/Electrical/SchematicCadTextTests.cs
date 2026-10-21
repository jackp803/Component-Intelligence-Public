using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadTextTests
{
    [Theory]
    [InlineData("%%p0.1%%D", "\u00b10.1\u00b0")]
    [InlineData("%%C5 / %%c3", "\u23005 / \u23003")]
    [InlineData("100%%%", "100%")]
    [InlineData("%%P %%d", "\u00b1 \u00b0")]
    [InlineData("%%uABC%%u %%q %%", "%%uABC%%u %%q %%")]
    [InlineData("%%%p", "%p")]
    [InlineData("X1 / INPUT_1", "X1 / INPUT_1")]
    public void DisplayDecodesSupportedSymbolsWithoutChangingStoredText(string source, string expected)
    {
        var primitive = new SchematicCadPrimitive { Kind = "TEXT", Start = new(0, 0), Text = source };
        Assert.Equal(expected, primitive.DisplayText);
        Assert.Equal(source, primitive.Text);
    }
}
