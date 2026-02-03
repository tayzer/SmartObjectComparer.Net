using System.IO;
using System.Text.Json;
using ComparisonTool.Core.RequestComparison.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class RequestFileParserServiceTests : IDisposable
{
    private readonly Mock<ILogger<RequestFileParserService>> loggerMock;
    private readonly RequestFileParserService service;
    private readonly List<string> createdPaths = new();

    public RequestFileParserServiceTests()
    {
        this.loggerMock = new Mock<ILogger<RequestFileParserService>>();
        this.service = new RequestFileParserService(this.loggerMock.Object);
    }

    public void Dispose()
    {
        foreach (var path in this.createdPaths)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    [TestMethod]
    [DataRow(".json", "application/json")]
    [DataRow(".xml", "application/xml")]
    [DataRow(".txt", "text/plain")]
    [DataRow(".html", "text/html")]
    [DataRow(".htm", "text/html")]
    [DataRow(".unknown", "text/plain")]
    [DataRow("", "text/plain")]
    public void GetContentType_ReturnsCorrectContentType(string extension, string expectedContentType)
    {
        var result = RequestFileParserService.GetContentType(extension);
        result.Should().Be(expectedContentType);
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_ThrowsWhenBatchNotFound()
    {
        var nonExistentBatchId = "nonexistent123";

        Func<Task> action = () => this.service.ParseRequestBatchAsync(nonExistentBatchId);

        await action.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_ParsesRequestFiles()
    {
        // Arrange
        var batchId = "parsebatch" + Guid.NewGuid().ToString("N")[..6];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
        this.createdPaths.Add(batchPath);

        // Create test request files
        var jsonFile = Path.Combine(batchPath, "request1.json");
        var xmlFile = Path.Combine(batchPath, "request2.xml");

        await File.WriteAllTextAsync(jsonFile, "{\"test\": 123}");
        await File.WriteAllTextAsync(xmlFile, "<test>123</test>");

        // Act
        var result = await this.service.ParseRequestBatchAsync(batchId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.RelativePath == "request1.json");
        result.Should().Contain(r => r.RelativePath == "request2.xml");
        result.Should().Contain(r => r.ContentType == "application/json");
        result.Should().Contain(r => r.ContentType == "application/xml");
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_LoadsSidecarHeaders()
    {
        // Arrange
        var batchId = "headerbatch" + Guid.NewGuid().ToString("N")[..6];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
        this.createdPaths.Add(batchPath);

        // Create request file and sidecar header file
        var requestFile = Path.Combine(batchPath, "request.json");
        var headerFile = Path.Combine(batchPath, "request.json.headers.json");

        await File.WriteAllTextAsync(requestFile, "{\"test\": 123}");
        var headers = new { headers = new Dictionary<string, string> { ["X-Custom"] = "value123" } };
        await File.WriteAllTextAsync(headerFile, JsonSerializer.Serialize(headers));

        // Act
        var result = await this.service.ParseRequestBatchAsync(batchId);

        // Assert
        result.Should().HaveCount(1);
        var request = result[0];
        request.RelativePath.Should().Be("request.json");
        request.Headers.Should().ContainKey("X-Custom");
        request.Headers["X-Custom"].Should().Be("value123");
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_IgnoresHeaderFiles()
    {
        // Arrange
        var batchId = "ignorebatch" + Guid.NewGuid().ToString("N")[..6];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);
        this.createdPaths.Add(batchPath);

        // Create request file and sidecar header file
        var requestFile = Path.Combine(batchPath, "request.json");
        var headerFile = Path.Combine(batchPath, "request.json.headers.json");

        await File.WriteAllTextAsync(requestFile, "{\"test\": 123}");
        await File.WriteAllTextAsync(headerFile, "{\"headers\": {}}");

        // Act
        var result = await this.service.ParseRequestBatchAsync(batchId);

        // Assert - should only include the request file, not the headers file
        result.Should().HaveCount(1);
        result[0].RelativePath.Should().Be("request.json");
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_PreservesSubdirectoryStructure()
    {
        // Arrange
        var batchId = "subdirbatch" + Guid.NewGuid().ToString("N")[..6];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        var subdir = Path.Combine(batchPath, "subdir");
        Directory.CreateDirectory(subdir);
        this.createdPaths.Add(batchPath);

        // Create request files in subdirectory
        var requestFile = Path.Combine(subdir, "nested.json");
        await File.WriteAllTextAsync(requestFile, "{\"nested\": true}");

        // Act
        var result = await this.service.ParseRequestBatchAsync(batchId);

        // Assert
        result.Should().HaveCount(1);
        result[0].RelativePath.Should().Be(Path.Combine("subdir", "nested.json"));
    }
}
