using System.IO;
using System.Text.Json;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

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
        result.ShouldBe(expectedContentType);
    }

    [TestMethod]
    public async Task ParseRequestBatchAsync_ThrowsWhenBatchNotFound()
    {
        var nonExistentBatchId = "nonexistent123";

        Func<Task> action = () => this.service.ParseRequestBatchAsync(nonExistentBatchId);

        await Should.ThrowAsync<DirectoryNotFoundException>(action);
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
        result.Count.ShouldBe(2);
        result.Any(r => r.RelativePath == "request1.json").ShouldBeTrue();
        result.Any(r => r.RelativePath == "request2.xml").ShouldBeTrue();
        result.Any(r => r.ContentType == "application/json").ShouldBeTrue();
        result.Any(r => r.ContentType == "application/xml").ShouldBeTrue();
        result.Single(r => r.RelativePath == "request1.json").DetectedFormat.ShouldBe(ComparisonTool.Core.Serialization.SerializationFormat.Json);
        result.Single(r => r.RelativePath == "request2.xml").DetectedFormat.ShouldBe(ComparisonTool.Core.Serialization.SerializationFormat.Xml);
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
        result.Count.ShouldBe(1);
        var request = result[0];
        request.RelativePath.ShouldBe("request.json");
        request.Headers.ContainsKey("X-Custom").ShouldBeTrue();
        request.Headers["X-Custom"].ShouldBe("value123");
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
        result.Count.ShouldBe(1);
        result[0].RelativePath.ShouldBe("request.json");
        result[0].DetectedFormat.ShouldBe(ComparisonTool.Core.Serialization.SerializationFormat.Json);
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
        result.Count.ShouldBe(1);
        result[0].RelativePath.ShouldBe(Path.Combine("subdir", "nested.json"));
    }
}
