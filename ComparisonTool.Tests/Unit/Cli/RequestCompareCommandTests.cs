using ComparisonTool.Cli.Commands;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Cli;

[TestClass]
public sealed class RequestCompareCommandTests : IDisposable
{
    private readonly List<string> createdPaths = new List<string>();

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
    public void CreateRequestBatchSelection_AppliesRangeAfterOrdinalSorting()
    {
        var requestDirectory = this.CreateRequestDirectory(
            "b.xml",
            "a.json",
            "c.txt",
            "z.headers.json",
            "ignore.csv");

        Directory.CreateDirectory(Path.Combine(requestDirectory.FullName, "nested"));
        File.WriteAllText(Path.Combine(requestDirectory.FullName, "nested", "nested.json"), "{}");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "2-3");

        selection.TotalEligibleFileCount.Should().Be(3);
        selection.SelectedFileCount.Should().Be(2);
        selection.AppliedRange.ToString().Should().Be("2-3");
        selection.SelectedFiles.Select(file => file.Name).Should().Equal("b.xml", "c.txt");
    }

    [TestMethod]
    public void CreateRequestBatchSelection_ClampsRangeEndBeyondAvailableCount()
    {
        var requestDirectory = this.CreateRequestDirectory("b.xml", "a.json", "c.txt");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "2-99");

        selection.TotalEligibleFileCount.Should().Be(3);
        selection.SelectedFileCount.Should().Be(2);
        selection.AppliedRange.ToString().Should().Be("2-3");
        selection.AppliedRangeDisplay.Should().Be("2-3 (requested 2-99)");
        selection.SelectedFiles.Select(file => file.Name).Should().Equal("b.xml", "c.txt");
    }

    [TestMethod]
    public void GetFilesToStage_IncludesSidecarsForSelectedFilesOnly()
    {
        var requestDirectory = this.CreateRequestDirectory(
            "001.json",
            "001.json.headers.json",
            "002.json",
            "002.json.headers.json",
            "003.json",
            "ignore.csv");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "2-2");

        var filesToStage = RequestCompareCommand.GetFilesToStage(selection);

        filesToStage.Select(file => file.Name).Should().Equal("002.json", "002.json.headers.json");
    }

    [TestMethod]
    [DataRow("abc", "Expected format")]
    [DataRow("0-2", "positive 1-based ordinals")]
    [DataRow("-1-2", "positive 1-based ordinals")]
    [DataRow("5-2", "less than or equal")]
    public void CreateRequestBatchSelection_RejectsMalformedOrInvalidRanges(string rangeText, string expectedMessage)
    {
        var requestDirectory = this.CreateRequestDirectory("a.json", "b.xml", "c.txt");

        Action action = () => RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, rangeText);

        action.Should().Throw<ArgumentException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [TestMethod]
    public void CreateRequestBatchSelection_RejectsRangeStartBeyondAvailableCount()
    {
        var requestDirectory = this.CreateRequestDirectory("a.json", "b.xml", "c.txt");

        Action action = () => RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "4-5");

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*exceeds the available eligible request file count 3*");
    }

        [TestMethod]
        public async Task LoadMaskRulesAsync_LoadsRulesFromArrayJson()
        {
                var file = this.CreateTempFile(
                        "mask-rules.json",
                        """
                        [
                            {
                                "propertyPath": "Order.Payments[*].CardNumber",
                                "preserveLastCharacters": 4
                            }
                        ]
                        """);

                var result = await RequestCompareCommand.LoadMaskRulesAsync(file, CancellationToken.None);

                result.IsSuccess.Should().BeTrue();
                result.MaskRules.Should().ContainSingle();
                result.MaskRules![0].PropertyPath.Should().Be("Order.Payments[*].CardNumber");
                result.MaskRules[0].PreserveLastCharacters.Should().Be(4);
                result.MaskRules[0].MaskCharacter.Should().Be("*");
        }

        [TestMethod]
        public async Task LoadMaskRulesAsync_LoadsRulesFromContainerJson()
        {
                var file = this.CreateTempFile(
                        "mask-rules-container.json",
                        """
                        {
                            "maskRules": [
                                {
                                    "propertyPath": "Order.Customer.Email",
                                    "maskCharacter": "#"
                                }
                            ]
                        }
                        """);

                var result = await RequestCompareCommand.LoadMaskRulesAsync(file, CancellationToken.None);

                result.IsSuccess.Should().BeTrue();
                result.MaskRules.Should().ContainSingle();
                result.MaskRules![0].MaskCharacter.Should().Be("#");
                result.MaskRules[0].PreserveLastCharacters.Should().Be(0);
        }

        [TestMethod]
        public async Task LoadMaskRulesAsync_RejectsInvalidMaskCharacter()
        {
                var file = this.CreateTempFile(
                        "mask-rules-invalid.json",
                        """
                        [
                            {
                                "propertyPath": "Order.Payments[*].CardNumber",
                                "maskCharacter": "XX"
                            }
                        ]
                        """);

                var result = await RequestCompareCommand.LoadMaskRulesAsync(file, CancellationToken.None);

                result.IsSuccess.Should().BeFalse();
                result.ErrorMessage.Should().Contain("exactly one character");
        }

            [TestMethod]
            public async Task LoadMaskRulesAsync_RejectsNullEntries()
            {
                var file = this.CreateTempFile(
                    "mask-rules-null.json",
                    """
                    [
                      null
                    ]
                    """);

                var result = await RequestCompareCommand.LoadMaskRulesAsync(file, CancellationToken.None);

                result.IsSuccess.Should().BeFalse();
                result.ErrorMessage.Should().Contain("cannot contain null entries");
            }

    private DirectoryInfo CreateRequestDirectory(params string[] fileNames)
    {
        var path = Path.Combine(Path.GetTempPath(), "ComparisonToolCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        this.createdPaths.Add(path);

        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(path, fileName), fileName);
        }

        return new DirectoryInfo(path);
    }

    private FileInfo CreateTempFile(string fileName, string contents)
    {
        var directory = this.CreateRequestDirectory();
        var path = Path.Combine(directory.FullName, fileName);
        File.WriteAllText(path, contents);
        return new FileInfo(path);
    }
}