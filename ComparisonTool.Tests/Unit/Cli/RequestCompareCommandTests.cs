using ComparisonTool.Cli.Commands;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

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
    public void CreateRequestBatchSelection_AppliesRangeAfterRecursiveOrdinalSorting()
    {
        var requestDirectory = this.CreateRequestDirectory(
            "b.xml",
            "a.json",
            "c.txt",
            "z.headers.json",
            "ignore.csv");

        var nestedDirectory = Path.Combine(requestDirectory.FullName, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "nested.json"), "{}");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "2-3");

        selection.TotalEligibleFileCount.ShouldBe(4);
        selection.SelectedFileCount.ShouldBe(2);
        selection.AppliedRange.ToString().ShouldBe("2-3");
        selection.SelectedFiles.Select(file => file.Name).ToArray().ShouldBe(new[] { "b.xml", "c.txt" });
    }

    [TestMethod]
    public void CreateRequestBatchSelection_PreservesDuplicateFileNamesInSubdirectories()
    {
        var requestDirectory = this.CreateRequestDirectory();
        var alphaDirectory = Path.Combine(requestDirectory.FullName, "alpha");
        var betaDirectory = Path.Combine(requestDirectory.FullName, "beta");
        Directory.CreateDirectory(alphaDirectory);
        Directory.CreateDirectory(betaDirectory);
        File.WriteAllText(Path.Combine(alphaDirectory, "lookup.xml"), "<request />");
        File.WriteAllText(Path.Combine(betaDirectory, "lookup.xml"), "<request />");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, null);

        selection.TotalEligibleFileCount.ShouldBe(2);
        selection.SelectedFiles
            .Select(file => Path.GetRelativePath(requestDirectory.FullName, file.FullName).Replace('\\', '/'))
            .ToArray()
            .ShouldBe(new[] { "alpha/lookup.xml", "beta/lookup.xml" });
    }

    [TestMethod]
    public void CreateRequestBatchSelection_ClampsRangeEndBeyondAvailableCount()
    {
        var requestDirectory = this.CreateRequestDirectory("b.xml", "a.json", "c.txt");

        var selection = RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "2-99");

        selection.TotalEligibleFileCount.ShouldBe(3);
        selection.SelectedFileCount.ShouldBe(2);
        selection.AppliedRange.ToString().ShouldBe("2-3");
        selection.AppliedRangeDisplay.ShouldBe("2-3 (requested 2-99)");
        selection.SelectedFiles.Select(file => file.Name).ToArray().ShouldBe(new[] { "b.xml", "c.txt" });
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

        filesToStage.Select(file => file.Name).ToArray().ShouldBe(new[] { "002.json", "002.json.headers.json" });
    }

    [TestMethod]
    public void ResolveEndpointReference_MatchesConfiguredNameCaseInsensitively()
    {
        var endpoints = new List<RequestComparisonEndpointOption>
        {
            new RequestComparisonEndpointOption
            {
                Name = "Local Mock Customer Lookup SOAP",
                Url = "http://localhost:5055/api/mock/customer-lookup/soap",
            },
        };

        var result = RequestCompareCommand.ResolveEndpointReference(
            "local mock customer lookup soap",
            endpoints,
            "Endpoint A");

        result.IsSuccess.ShouldBeTrue();
        result.Url.ShouldBe("http://localhost:5055/api/mock/customer-lookup/soap");
        result.EndpointOption.ShouldBeSameAs(endpoints[0]);
    }

    [TestMethod]
    public void ResolveEndpointReference_AcceptsAbsoluteUrlWithoutConfiguredEndpoint()
    {
        var result = RequestCompareCommand.ResolveEndpointReference(
            "https://service.example.com/api",
            Array.Empty<RequestComparisonEndpointOption>(),
            "Endpoint A");

        result.IsSuccess.ShouldBeTrue();
        result.Url.ShouldBe("https://service.example.com/api");
        result.EndpointOption.ShouldBeNull();
    }

    [TestMethod]
    public void TryParseHeader_ParsesColonSeparatedHeader()
    {
        var parsed = RequestCompareCommand.TryParseHeader(
            "X-Trace: abc:123",
            out var header,
            out var errorMessage);

        parsed.ShouldBeTrue();
        errorMessage.ShouldBeNull();
        header.Key.ShouldBe("X-Trace");
        header.Value.ShouldBe("abc:123");
    }

    [TestMethod]
    public void ShouldUseAlternateContract_ProfileSelectionImpliesAlternateMode()
    {
        var options = new RequestCompareCommand.RequestCompareCliOptions
        {
            RequestDirectory = new DirectoryInfo(Path.GetTempPath()),
            ModelName = "Model",
            AlternateContractProfileId = "profile-a",
        };

        RequestCompareCommand.ShouldUseAlternateContract(options).ShouldBeTrue();
    }

    [TestMethod]
    public void ResolveAlternateContractProfile_ReturnsAvailableChoicesOnValidationFailure()
    {
        var options = new RequestCompareCommand.RequestCompareCliOptions
        {
            RequestDirectory = new DirectoryInfo(Path.GetTempPath()),
            ModelName = "Model",
            UseAlternateContract = true,
            AlternateContractProfileId = "missing-profile",
        };
        var registry = new FakeAlternateContractProfileRegistry(
            "Alternate contract profile 'missing-profile' is not registered.",
            new[] { "profile-a", "profile-b" });

        var result = RequestCompareCommand.ResolveAlternateContractProfile(options, registry);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("missing-profile");
        result.AvailableProfileIds.ShouldBe(new[] { "profile-a", "profile-b" });
    }
    [TestMethod]
    public async Task BuildHeadersAsync_AppliesEndpointDefaultsAndCliPrecedence()
    {
        var commonHeaderFile = this.CreateTempFile(
            "headers.json",
            """
            {
              "headers": {
                "X-Default": "common-file",
                "X-File": "both"
              }
            }
            """);
        var endpointAHeaderFile = this.CreateTempFile(
            "headers-a.json",
            """
            {
              "X-File": "a-file",
              "X-A-Only": "from-file"
            }
            """);
        var endpointBHeaderFile = this.CreateTempFile(
            "headers-b.json",
            """
            {
              "headers": {
                "X-File": "b-file",
                "X-B-Only": "from-file"
              }
            }
            """);

        var options = new RequestCompareCommand.RequestCompareCliOptions
        {
            RequestDirectory = new DirectoryInfo(Path.GetTempPath()),
            ModelName = "Model",
            HeadersFile = commonHeaderFile,
            HeadersAFile = endpointAHeaderFile,
            HeadersBFile = endpointBHeaderFile,
            Headers = new[] { "X-Default: common-inline", "X-Inline: both" },
            HeadersA = new[] { "X-File: a-inline" },
            HeadersB = new[] { "X-File: b-inline" },
            SoapAction = "urn:test",
        };
        var endpointA = new RequestComparisonEndpointOption
        {
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Default"] = "a-default",
                ["X-A-Default"] = "a-default",
            },
        };
        var endpointB = new RequestComparisonEndpointOption
        {
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Default"] = "b-default",
                ["X-B-Default"] = "b-default",
            },
        };

        var result = await RequestCompareCommand.BuildHeadersAsync(
            options,
            endpointA,
            endpointB,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.HeadersA["X-Default"].ShouldBe("common-inline");
        result.HeadersB["X-Default"].ShouldBe("common-inline");
        result.HeadersA["X-File"].ShouldBe("a-inline");
        result.HeadersB["X-File"].ShouldBe("b-inline");
        result.HeadersA["X-A-Default"].ShouldBe("a-default");
        result.HeadersB["X-B-Default"].ShouldBe("b-default");
        result.HeadersA["X-Inline"].ShouldBe("both");
        result.HeadersB["X-Inline"].ShouldBe("both");
        result.HeadersA["SOAPAction"].ShouldBe("urn:test");
        result.HeadersB["SOAPAction"].ShouldBe("urn:test");
        result.HeadersA["X-A-Only"].ShouldBe("from-file");
        result.HeadersB["X-B-Only"].ShouldBe("from-file");
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

        var exception = Should.Throw<ArgumentException>(action);
        exception.Message.ShouldContain(expectedMessage);
    }

    [TestMethod]
    public void CreateRequestBatchSelection_RejectsRangeStartBeyondAvailableCount()
    {
        var requestDirectory = this.CreateRequestDirectory("a.json", "b.xml", "c.txt");

        Action action = () => RequestCompareCommand.CreateRequestBatchSelection(requestDirectory, "4-5");

        var exception = Should.Throw<ArgumentOutOfRangeException>(action);
        exception.Message.ShouldContain("exceeds the available eligible request file count 3");
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

        result.IsSuccess.ShouldBeTrue();
        result.MaskRules.ShouldNotBeNull();
        result.MaskRules!.Count.ShouldBe(1);
        result.MaskRules[0].PropertyPath.ShouldBe("Order.Payments[*].CardNumber");
        result.MaskRules[0].PreserveLastCharacters.ShouldBe(4);
        result.MaskRules[0].MaskCharacter.ShouldBe("*");
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

        result.IsSuccess.ShouldBeTrue();
        result.MaskRules.ShouldNotBeNull();
        result.MaskRules!.Count.ShouldBe(1);
        result.MaskRules[0].MaskCharacter.ShouldBe("#");
        result.MaskRules[0].PreserveLastCharacters.ShouldBe(0);
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

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("exactly one character");
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

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("cannot contain null entries");
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

    private sealed class FakeAlternateContractProfileRegistry : IRequestComparisonAlternateContractProfileRegistry
    {
        private readonly string errorMessage;
        private readonly IReadOnlyList<string> profileIds;

        public FakeAlternateContractProfileRegistry(string errorMessage, IReadOnlyList<string> profileIds)
        {
            this.errorMessage = errorMessage;
            this.profileIds = profileIds;
        }

        public void Register(RequestComparisonAlternateContractProfile profile)
        {
            throw new NotSupportedException();
        }

        public RequestComparisonAlternateContractProfile Resolve(string canonicalModelName, string? profileId = null)
        {
            throw new NotSupportedException();
        }

        public bool TryResolve(
            string canonicalModelName,
            string? profileId,
            out RequestComparisonAlternateContractProfile? profile,
            out string? errorMessage)
        {
            profile = null;
            errorMessage = this.errorMessage;
            return false;
        }

        public IReadOnlyList<string> GetProfileIds(string canonicalModelName)
        {
            return this.profileIds;
        }
    }
}
