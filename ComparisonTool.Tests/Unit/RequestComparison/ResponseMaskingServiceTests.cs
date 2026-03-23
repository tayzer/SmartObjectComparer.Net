using System.Text;
using System.Text.Json;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public sealed class ResponseMaskingServiceTests : IDisposable
{
    private readonly List<string> createdPaths = new List<string>();
    private readonly ResponseMaskingService service;

    public ResponseMaskingServiceTests()
    {
        var logger = new Mock<ILogger<ResponseMaskingService>>();
        service = new ResponseMaskingService(logger.Object);
    }

    public void Dispose()
    {
        foreach (var path in createdPaths)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    [TestMethod]
    public async Task MaskResponsesAsync_MasksJsonStringValuesUsingWildcardPaths()
    {
        var responseFile = this.CreateTempFile(
            "response.txt",
            """
            {
              "order": {
                "payments": [
                  {
                    "cardNumber": "4111111111111111",
                    "amount": 12.34
                  },
                  {
                    "cardNumber": "5555555555554444",
                    "amount": 56.78
                  }
                ]
              }
            }
            """);

        var executionResult = CreateExecutionResult(responseFile.FullName, "application/json");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.Payments[*].CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        await service.MaskResponsesAsync(new[] { executionResult }, rules, CancellationToken.None);

        var maskedJson = await File.ReadAllTextAsync(responseFile.FullName);
        maskedJson.Should().NotContain("4111111111111111");
        maskedJson.Should().NotContain("5555555555554444");
        maskedJson.Should().Contain("************1111");
        maskedJson.Should().Contain("************4444");

        using var document = JsonDocument.Parse(maskedJson);
        document.RootElement
            .GetProperty("order")
            .GetProperty("payments")[0]
            .GetProperty("amount")
            .GetDecimal()
            .Should().Be(12.34m);
    }

    [TestMethod]
    public async Task MaskResponsesAsync_MasksXmlLeafElementsIgnoringNamespaces()
    {
        var responseFile = this.CreateTempFile(
            "response.xml",
            """
            <ns:Order xmlns:ns="urn:test">
              <ns:Payment>
                <ns:CardNumber>4111111111111111</ns:CardNumber>
              </ns:Payment>
              <ns:Payment>
                <ns:CardNumber>5555555555554444</ns:CardNumber>
              </ns:Payment>
            </ns:Order>
            """);

        var executionResult = CreateExecutionResult(responseFile.FullName, "application/xml");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.Payment[*].CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "#",
            },
        };

        await service.MaskResponsesAsync(new[] { executionResult }, rules, CancellationToken.None);

        var maskedXml = await File.ReadAllTextAsync(responseFile.FullName);
        maskedXml.Should().NotContain("4111111111111111");
        maskedXml.Should().NotContain("5555555555554444");
        maskedXml.Should().Contain("############1111");
        maskedXml.Should().Contain("############4444");
    }

    [TestMethod]
    public async Task MaskResponsesAsync_PreservesUtf16XmlEncoding()
    {
        var responseFile = this.CreateTempFile("utf16-response.xml", string.Empty);
        var xml = """
            <?xml version="1.0" encoding="utf-16"?>
            <Order>
              <Payment>
                <CardNumber>4111111111111111</CardNumber>
              </Payment>
            </Order>
            """;
        await File.WriteAllTextAsync(responseFile.FullName, xml, Encoding.Unicode);

        var executionResult = CreateExecutionResult(responseFile.FullName, "application/xml; charset=utf-16");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.Payment.CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        await service.MaskResponsesAsync(new[] { executionResult }, rules, CancellationToken.None);

        var maskedXml = await File.ReadAllTextAsync(responseFile.FullName, Encoding.Unicode);
        maskedXml.Should().Contain("************1111");
        maskedXml.Should().NotContain("4111111111111111");
    }

    [TestMethod]
    public async Task MaskResponsesAsync_MasksJsonWhenContentTypeIsTextPlain()
    {
        var responseFile = this.CreateTempFile(
            "response.txt",
            """
            { "order": { "cardNumber": "4111111111111111" } }
            """);

        var executionResult = CreateExecutionResult(responseFile.FullName, "text/plain");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        await service.MaskResponsesAsync(new[] { executionResult }, rules, CancellationToken.None);

        var maskedJson = await File.ReadAllTextAsync(responseFile.FullName);
        maskedJson.Should().Contain("************1111");
        maskedJson.Should().NotContain("4111111111111111");
    }

    [TestMethod]
    public async Task MaskResponsesAsync_MasksSingletonXmlElementWhenRuleUsesWildcard()
    {
        var responseFile = this.CreateTempFile(
            "response.xml",
            """
            <Order>
              <Payment>
                <CardNumber>4111111111111111</CardNumber>
              </Payment>
            </Order>
            """);

        var executionResult = CreateExecutionResult(responseFile.FullName, "application/xml");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.Payment[*].CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        await service.MaskResponsesAsync(new[] { executionResult }, rules, CancellationToken.None);

        var maskedXml = await File.ReadAllTextAsync(responseFile.FullName);
        maskedXml.Should().Contain("************1111");
        maskedXml.Should().NotContain("4111111111111111");
    }

    [TestMethod]
    public void MaskContent_ReturnsOriginalBytesForInvalidJson()
    {
        var original = Encoding.UTF8.GetBytes("<html>not json</html>");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        var masked = service.MaskContent(original, "application/json", "response.json", rules);

        masked.Should().Equal(original);
    }

    [TestMethod]
    public void MaskContent_ReturnsOriginalBytesForInvalidXml()
    {
        var original = Encoding.UTF8.GetBytes("{ \"not\": \"xml\" }");
        var rules = new List<MaskRuleDto>
        {
            new MaskRuleDto
            {
                PropertyPath = "Order.Payment.CardNumber",
                PreserveLastCharacters = 4,
                MaskCharacter = "*",
            },
        };

        var masked = service.MaskContent(original, "application/xml", "response.xml", rules);

        masked.Should().Equal(original);
    }

    private static RequestExecutionResult CreateExecutionResult(string responsePathA, string contentTypeA)
        => new RequestExecutionResult
        {
            Request = new RequestFileInfo
            {
                RelativePath = "request.json",
                FilePath = responsePathA,
                ContentType = "application/json",
            },
            Success = true,
            StatusCodeA = 200,
            StatusCodeB = 200,
            ResponsePathA = responsePathA,
            ContentTypeA = contentTypeA,
        };

    private FileInfo CreateTempFile(string fileName, string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), "ComparisonToolMaskingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        this.createdPaths.Add(path);

        var filePath = Path.Combine(path, fileName);
        File.WriteAllText(filePath, contents);
        return new FileInfo(filePath);
    }
}