using ComparisonTool.Core.Utilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Utilities;

[TestClass]
public class StructuredTextDisplayFormatterTests
{
    [TestMethod]
    public void FormatForDisplay_ShouldPrettyPrintJson()
    {
        const string json = "{\"name\":\"Alice\",\"items\":[1,2]}";

        var formatted = StructuredTextDisplayFormatter.FormatForDisplay(json, "application/json", "payload.json");

        formatted.Should().Contain("\n");
        formatted.Should().Contain("  \"name\": \"Alice\"");
        formatted.Should().Contain("  \"items\": [");
    }

    [TestMethod]
    public void FormatForDisplay_ShouldPrettyPrintXml()
    {
        const string xml = "<root><item id=\"1\">value</item><item id=\"2\">other</item></root>";

        var formatted = StructuredTextDisplayFormatter.FormatForDisplay(xml, "application/xml", "payload.xml");

        formatted.Should().Contain("\n");
        formatted.Should().Contain("<root>");
        formatted.Should().Contain("  <item id=\"1\">value</item>");
        formatted.Should().Contain("  <item id=\"2\">other</item>");
    }

    [TestMethod]
    public void FormatForDisplay_ShouldReturnOriginalText_WhenContentIsInvalid()
    {
        const string invalidJson = "{not valid json}";

        var formatted = StructuredTextDisplayFormatter.FormatForDisplay(invalidJson, "application/json", "payload.json");

        formatted.Should().Be(invalidJson);
    }
}