// <copyright file="FileSystemServiceTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.IO;
using ComparisonTool.Core.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.Utilities;

[TestClass]
public class FileSystemServiceTests : IDisposable
{
    private readonly Mock<ILogger<FileSystemService>> mockLogger;
    private readonly FileSystemService service;
    private readonly string testDirectory;

    public FileSystemServiceTests()
    {
        this.mockLogger = new Mock<ILogger<FileSystemService>>();
        this.service = new FileSystemService(this.mockLogger.Object);
        this.testDirectory = Path.Combine(Path.GetTempPath(), "ComparisonToolTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(this.testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.testDirectory))
        {
            Directory.Delete(this.testDirectory, true);
        }
    }

    [TestMethod]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        this.service.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GetXmlFilesFromDirectoryAsync_WithValidDirectory_ShouldReturnFiles()
    {
        // Arrange
        var testFile = Path.Combine(this.testDirectory, "testfile.xml");
        File.WriteAllText(testFile, "<test>content</test>");

        // Act
        var files = await this.service.GetXmlFilesFromDirectoryAsync(this.testDirectory);

        // Assert
        files.Should().NotBeNull();
        files.Should().Contain(f => Path.GetFileName(f.FilePath) == "testfile.xml");
    }

    [TestMethod]
    public async Task GetXmlFilesFromDirectoryAsync_WithNonExistentDirectory_ShouldThrowException()
    {
        // Arrange
        var nonExistentDir = Path.Combine(this.testDirectory, "NonExistentDirectory");

        // Act & Assert
        var action = () => this.service.GetXmlFilesFromDirectoryAsync(nonExistentDir);
        await action.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [TestMethod]
    public async Task GetFileAsMemoryStreamAsync_WithExistingFile_ShouldReturnStream()
    {
        // Arrange
        var testFile = Path.Combine(this.testDirectory, "testfile.txt");
        var content = "test content";
        File.WriteAllText(testFile, content);

        // Act
        using var stream = await this.service.GetFileAsMemoryStreamAsync(testFile);

        // Assert
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        result.Should().Be(content);
    }

    [TestMethod]
    public async Task GetFileAsMemoryStreamAsync_WithNonExistentFile_ShouldThrowException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(this.testDirectory, "NonExistentFile.txt");

        // Act & Assert
        var action = () => this.service.GetFileAsMemoryStreamAsync(nonExistentFile);
        await action.Should().ThrowAsync<FileNotFoundException>();
    }

    [TestMethod]
    public async Task OpenFileStreamAsync_WithExistingFile_ShouldReturnStream()
    {
        // Arrange
        var testFile = Path.Combine(this.testDirectory, "testfile.txt");
        var content = "test content";
        File.WriteAllText(testFile, content);

        // Act
        using var stream = await this.service.OpenFileStreamAsync(testFile);

        // Assert
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        result.Should().Be(content);
    }

    [TestMethod]
    public async Task OpenFileStreamAsync_WithNonExistentFile_ShouldThrowException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(this.testDirectory, "NonExistentFile.txt");

        // Act & Assert
        var action = () => this.service.OpenFileStreamAsync(nonExistentFile);
        await action.Should().ThrowAsync<FileNotFoundException>();
    }

    [TestMethod]
    public async Task CreateFilePairsAsync_WithMatchingFiles_ShouldCreatePairs()
    {
        // Arrange
        var tempDir1 = Path.Combine(this.testDirectory, "TestDir1");
        var tempDir2 = Path.Combine(this.testDirectory, "TestDir2");

        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);

        var file1 = Path.Combine(tempDir1, "test.xml");
        var file2 = Path.Combine(tempDir2, "test.xml");

        File.WriteAllText(file1, "<test>content1</test>");
        File.WriteAllText(file2, "<test>content2</test>");

        // Act
        var pairs = await this.service.CreateFilePairsAsync(tempDir1, tempDir2);

        // Assert
        pairs.Should().NotBeNull();
        pairs.Should().HaveCount(1);
        pairs[0].File1Path.Should().Be(file1);
        pairs[0].File2Path.Should().Be(file2);
        pairs[0].RelativePath.Should().Be("test.xml");
    }

    [TestMethod]
    public async Task CreateFilePairsAsync_WithNonExistentDirectory_ShouldThrowException()
    {
        // Arrange
        var nonExistentDir = Path.Combine(this.testDirectory, "NonExistentDirectory");
        var tempDir = Path.Combine(this.testDirectory, "ExistingDir");
        Directory.CreateDirectory(tempDir);

        // Act & Assert
        var action = () => this.service.CreateFilePairsAsync(nonExistentDir, tempDir);
        await action.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [TestMethod]
    public async Task MapFilesByFolderAsync_WithValidFiles_ShouldMapCorrectly()
    {
        // Arrange
        var files = new List<(MemoryStream Stream, string FileName)>
        {
            (new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content1")), "file1.xml"),
            (new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content2")), "folder/file2.xml"),
            (new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content3")), "folder/subfolder/file3.xml"),
        };

        try
        {
            // Act
            var result = await this.service.MapFilesByFolderAsync(files);

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainKey(string.Empty);
            result.Should().ContainKey("folder");
            result.Should().ContainKey("folder/subfolder");

            result[string.Empty].Should().HaveCount(1);
            result["folder"].Should().HaveCount(1);
            result["folder/subfolder"].Should().HaveCount(1);
        }
        finally
        {
            // Cleanup
            foreach (var (stream, _) in files)
            {
                stream.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task MapFilesByFolderAsync_WithEmptyList_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var files = new List<(MemoryStream Stream, string FileName)>();

        // Act
        var result = await this.service.MapFilesByFolderAsync(files);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
