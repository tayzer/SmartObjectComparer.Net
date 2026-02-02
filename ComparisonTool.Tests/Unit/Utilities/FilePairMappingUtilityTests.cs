// <copyright file="FilePairMappingUtilityTests.cs" company="PlaceholderCompany">



using System;
using System.Collections.Generic;
using System.Linq;
using ComparisonTool.Core.Utilities;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Utilities;

[TestClass]
public class FilePairMappingUtilityTests
{
    [TestMethod]
    public void CreateFilePairMappings_WithValidInputs_ShouldReturnCorrectMappings()
    {
        // Arrange
        var folder1Files = new List<string>
        {
            @"C:\Folder1\file1.xml",
            @"C:\Folder1\file2.xml",
            @"C:\Folder1\file3.xml",
        };
        var folder2Files = new List<string>
        {
            @"C:\Folder2\file1.xml",
            @"C:\Folder2\file2.xml",
            @"C:\Folder2\file3.xml",
        };

        // Act
        var result = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be(("C:\\Folder1\\file1.xml", "C:\\Folder2\\file1.xml", "file1.xml"));
        result[1].Should().Be(("C:\\Folder1\\file2.xml", "C:\\Folder2\\file2.xml", "file2.xml"));
        result[2].Should().Be(("C:\\Folder1\\file3.xml", "C:\\Folder2\\file3.xml", "file3.xml"));
    }

    [TestMethod]
    public void CreateFilePairMappings_WithUnorderedFiles_ShouldSortByName()
    {
        // Arrange
        var folder1Files = new List<string>
        {
            @"C:\Folder1\file3.xml",
            @"C:\Folder1\file1.xml",
            @"C:\Folder1\file2.xml",
        };
        var folder2Files = new List<string>
        {
            @"C:\Folder2\file2.xml",
            @"C:\Folder2\file1.xml",
            @"C:\Folder2\file3.xml",
        };

        // Act
        var result = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be(("C:\\Folder1\\file1.xml", "C:\\Folder2\\file1.xml", "file1.xml"));
        result[1].Should().Be(("C:\\Folder1\\file2.xml", "C:\\Folder2\\file2.xml", "file2.xml"));
        result[2].Should().Be(("C:\\Folder1\\file3.xml", "C:\\Folder2\\file3.xml", "file3.xml"));
    }

    [TestMethod]
    public void CreateFilePairMappings_WithDifferentCounts_ShouldUseMinimumCount()
    {
        // Arrange
        var folder1Files = new List<string>
        {
            @"C:\Folder1\file1.xml",
            @"C:\Folder1\file2.xml",
        };
        var folder2Files = new List<string>
        {
            @"C:\Folder2\file1.xml",
            @"C:\Folder2\file2.xml",
            @"C:\Folder2\file3.xml",
        };

        // Act
        var result = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be(("C:\\Folder1\\file1.xml", "C:\\Folder2\\file1.xml", "file1.xml"));
        result[1].Should().Be(("C:\\Folder1\\file2.xml", "C:\\Folder2\\file2.xml", "file2.xml"));
    }

    [TestMethod]
    public void CreateFilePairMappings_WithEmptyLists_ShouldReturnEmptyList()
    {
        // Arrange
        var folder1Files = new List<string>();
        var folder2Files = new List<string>();

        // Act
        var result = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);

        // Assert
        result.Should().BeEmpty();
    }

    [TestMethod]
    public void CreateFilePairMappings_WithNullFolder1Files_ShouldThrowArgumentNullException()
    {
        // Arrange
        List<string> folder1Files = null;
        var folder2Files = new List<string> { @"C:\Folder2\file1.xml" };

        // Act & Assert
        var action = () => FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);
        action.Should().Throw<ArgumentNullException>().WithParameterName("folder1Files");
    }

    [TestMethod]
    public void CreateFilePairMappings_WithNullFolder2Files_ShouldThrowArgumentNullException()
    {
        // Arrange
        var folder1Files = new List<string> { @"C:\Folder1\file1.xml" };
        List<string> folder2Files = null;

        // Act & Assert
        var action = () => FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);
        action.Should().Throw<ArgumentNullException>().WithParameterName("folder2Files");
    }

    [TestMethod]
    public void CreateFilePairMappings_WithComplexFileNames_ShouldHandleCorrectly()
    {
        // Arrange
        var folder1Files = new List<string>
        {
            @"C:\Folder1\file with spaces.xml",
            @"C:\Folder1\file-with-dashes.xml",
            @"C:\Folder1\file_with_underscores.xml",
        };
        var folder2Files = new List<string>
        {
            @"C:\Folder2\file with spaces.xml",
            @"C:\Folder2\file-with-dashes.xml",
            @"C:\Folder2\file_with_underscores.xml",
        };

        // Act
        var result = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);

        // Assert
        result.Should().HaveCount(3);

        // Files are sorted alphabetically by filename, so the order is:
        // 1. "file with spaces.xml" (space comes first in ASCII)
        // 2. "file_with_underscores.xml" (underscore comes before dash in ASCII)
        // 3. "file-with-dashes.xml" (dash comes last in ASCII)
        result[0].Should().Be(("C:\\Folder1\\file with spaces.xml", "C:\\Folder2\\file with spaces.xml", "file with spaces.xml"));
        result[1].Should().Be(("C:\\Folder1\\file_with_underscores.xml", "C:\\Folder2\\file_with_underscores.xml", "file_with_underscores.xml"));
        result[2].Should().Be(("C:\\Folder1\\file-with-dashes.xml", "C:\\Folder2\\file-with-dashes.xml", "file-with-dashes.xml"));
    }
}
