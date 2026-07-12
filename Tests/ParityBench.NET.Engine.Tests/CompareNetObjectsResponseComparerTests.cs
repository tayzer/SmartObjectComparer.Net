using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class CompareNetObjectsResponseComparerTests
{
    [TestMethod]
    public async Task CompareAsync_WhenObjectsAreEqualButRawHashesDiffer_ReturnsEqual()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Alpha" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
        Assert.AreEqual(0, result.DifferenceCount);
    }

    [TestMethod]
    public async Task CompareAsync_WhenObjectsDiffer_ReturnsDifferentWithMetadata()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Beta" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.IsTrue(result.DifferenceCount > 0);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath.Contains("Name", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreCompleteRuleMatchesDifference_ReturnsEqual()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Beta" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Name") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringStringCase_SuppressesCasingOnlyDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "alpha" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreStringCase: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringTrailingWhitespace_SuppressesEndWhitespaceDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Description = "Hello" }),
            ("b", () => new SampleResponse { Id = 1, Description = "Hello   " }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreTrailingWhitespaceAtEnd: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenTreatingNullAndEmptyCollectionsAsEqual_SuppressesCollectionDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Values = null }),
            ("b", () => new SampleResponse { Id = 1, Values = new List<int>() }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(treatNullAndEmptyCollectionsAsEqual: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences);
    }

    [TestMethod]
    public async Task CompareAsync_WhenUnorderedApplicantsDifferOnlyByIgnoredNestedRules_DoesNotReportMissingApplicants()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ReportResponseModel
            {
                IsAThing = true,
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "1", Name = "Alice", Score = 100, Address = new ApplicantAddress { Line1 = "old-a", Postcode = "AA1" } },
                    new Applicant { Id = "2", Name = "Bob", Score = 200, Address = new ApplicantAddress { Line1 = "old-b", Postcode = "BB1" } },
                },
            }),
            ("b", () => new ReportResponseModel
            {
                IsAThing = false,
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "2", Name = "Bob", Score = 999, Address = new ApplicantAddress { Line1 = "new-b", Postcode = "BB1" } },
                    new Applicant { Id = "1", Name = "Alice", Score = 888, Address = new ApplicantAddress { Line1 = "new-a", Postcode = "AA1" } },
                },
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: new[]
                {
                    new IgnoreRuleDefinition("Applicants[*].Score"),
                    new IgnoreRuleDefinition("Applicants[*].Address.Line1"),
                })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences);
        Assert.IsFalse(differences.Contains("null", StringComparison.OrdinalIgnoreCase), differences);
    }

    [TestMethod]
    public async Task CompareAsync_WhenUnorderedApplicantsHaveRealSiblingDifference_ReturnsRealApplicantDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ReportResponseModel
            {
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "1", Name = "Alice", Score = 100, Address = new ApplicantAddress { Line1 = "old-a", Postcode = "AA1" } },
                    new Applicant { Id = "2", Name = "Bob", Score = 200, Address = new ApplicantAddress { Line1 = "old-b", Postcode = "BB1" } },
                },
            }),
            ("b", () => new ReportResponseModel
            {
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "2", Name = "Bob", Score = 999, Address = new ApplicantAddress { Line1 = "new-b", Postcode = "BB1" } },
                    new Applicant { Id = "1", Name = "Alicia", Score = 888, Address = new ApplicantAddress { Line1 = "new-a", Postcode = "AA1" } },
                },
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: new[]
                {
                    new IgnoreRuleDefinition("Applicants[*].Score"),
                    new IgnoreRuleDefinition("Applicants[*].Address.Line1"),
                })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome, differences);
        Assert.AreEqual(1, result.Differences.Count, differences);
        StringAssert.Contains(result.Differences.Single().PropertyPath, "Name");
        Assert.IsFalse(differences.Contains("null", StringComparison.OrdinalIgnoreCase), differences);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringCollectionOrder_SuppressesReorderedCollectionDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Values = new List<int> { 1, 2, 3 } }),
            ("b", () => new SampleResponse { Id = 1, Values = new List<int> { 3, 2, 1 } }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreCollectionOrder: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringCollectionOrder_ReordersArrayOfComplexObjectsWithNoIgnoreRules_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ReportResponseModel
            {
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "1", Name = "Alice", Score = 100, Address = new ApplicantAddress { Line1 = "a", Postcode = "AA1" } },
                    new Applicant { Id = "2", Name = "Bob", Score = 200, Address = new ApplicantAddress { Line1 = "b", Postcode = "BB1" } },
                },
            }),
            ("b", () => new ReportResponseModel
            {
                Details = new ReportDetails { ReportId = "r-1" },
                Applicants = new[]
                {
                    new Applicant { Id = "2", Name = "Bob", Score = 200, Address = new ApplicantAddress { Line1 = "b", Postcode = "BB1" } },
                    new Applicant { Id = "1", Name = "Alice", Score = 100, Address = new ApplicantAddress { Line1 = "a", Postcode = "AA1" } },
                },
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreCollectionOrder: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringCollectionOrder_WithMixedCollectionTypes_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new MixedCollectionResponse
            {
                Numbers = new List<int> { 1, 2, 3 },
                Codes = new[] { "x", "y", "z" },
                Applicants = new List<Applicant>
                {
                    new Applicant { Id = "1", Name = "Alice", Score = 100 },
                    new Applicant { Id = "2", Name = "Bob", Score = 200 },
                },
                Tags = new HashSet<string> { "alpha", "beta", "gamma" },
            }),
            ("b", () => new MixedCollectionResponse
            {
                Numbers = new List<int> { 3, 2, 1 },
                Codes = new[] { "z", "y", "x" },
                Applicants = new List<Applicant>
                {
                    new Applicant { Id = "2", Name = "Bob", Score = 200 },
                    new Applicant { Id = "1", Name = "Alice", Score = 100 },
                },
                Tags = new HashSet<string> { "gamma", "alpha", "beta" },
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreCollectionOrder: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences + " | ERROR: " + result.ErrorMessage);
    }

    [TestMethod]
    public async Task CompareAsync_WhenSmartIgnoreMatchesPropertyName_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, CorrelationId = "a" }),
            ("b", () => new SampleResponse { Id = 1, CorrelationId = "b" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "CorrelationId") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenSmartIgnoreMatchesNamePattern_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, CorrelationId = "a" }),
            ("b", () => new SampleResponse { Id = 1, CorrelationId = "b" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.NamePattern, ".*CorrelationId$") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreRuleMatchesNestedSibling_ReturnsRemainingSiblingDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ComplexResponse { Detail = new ComplexDetail { Ignored = "ignore-a", Remaining = "left" } }),
            ("b", () => new ComplexResponse { Detail = new ComplexDetail { Ignored = "ignore-b", Remaining = "right" } }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Detail.Ignored") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(1, result.Differences.Count);
        Assert.AreEqual("Detail.Remaining", result.Differences.Single().PropertyPath);
    }

    [TestMethod]
    public async Task CompareAsync_WhenManyIgnoredDifferencesExist_StillReturnsNonIgnoredDifferences()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ManyIgnoredResponse
            {
                Items = Enumerable.Range(0, 20).Select(index => new ManyIgnoredItem { Ignored = $"a-{index}", Shared = index }).ToList(),
                Visible = "left",
            }),
            ("b", () => new ManyIgnoredResponse
            {
                Items = Enumerable.Range(0, 20).Select(index => new ManyIgnoredItem { Ignored = $"b-{index}", Shared = index }).ToList(),
                Visible = "right",
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                maxDifferences: 2,
                ignoreRules: new[] { new IgnoreRuleDefinition("Items[*].Ignored") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        string differencePaths = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath.Contains("Visible", StringComparison.Ordinal)), differencePaths);
        Assert.IsFalse(result.Differences.Any(difference => difference.PropertyPath.Contains("Ignored", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Differences.Count <= 2);
    }

    [TestMethod]
    public async Task CompareAsync_WhenV2ComparisonProducesDuplicateNormalizedDifference_CollapsesToSingleDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new DuplicateDiffRootResponse
            {
                Applicants =
                [
                    new DuplicateDiffApplicant
                    {
                        Datasets =
                        [
                            new DuplicateDiffDataset
                            {
                                Accounts =
                                [
                                    new DuplicateDiffAccount
                                    {
                                        AccountDetails = new DuplicateDiffAccountDetails
                                        {
                                            AccountGroupId = new DuplicateDiffAccountGroup { Id = "A" },
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
                TraceId = "same",
            }),
            ("b", () => new DuplicateDiffRootResponse
            {
                Applicants =
                [
                    new DuplicateDiffApplicant
                    {
                        Datasets =
                        [
                            new DuplicateDiffDataset
                            {
                                Accounts =
                                [
                                    new DuplicateDiffAccount
                                    {
                                        AccountDetails = new DuplicateDiffAccountDetails
                                        {
                                            AccountGroupId = new DuplicateDiffAccountGroup { Id = "B" },
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
                TraceId = "same",
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(1, result.Differences.Count);
        Assert.IsTrue(
            result.Differences[0].PropertyPath.Contains("Applicants[0].Datasets[0].Accounts[0].AccountDetails.AccountGroupId.Id", StringComparison.Ordinal),
            string.Join(" | ", result.Differences.Select(difference => difference.PropertyPath)));
    }

    [TestMethod]
    public async Task CompareAsync_WhenDuplicateDifferenceExistsAndMaxDifferencesIsApplied_DeduplicatesBeforeCap()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new DuplicateDiffRootResponse
            {
                Applicants =
                [
                    new DuplicateDiffApplicant
                    {
                        Datasets =
                        [
                            new DuplicateDiffDataset
                            {
                                Accounts =
                                [
                                    new DuplicateDiffAccount
                                    {
                                        AccountDetails = new DuplicateDiffAccountDetails
                                        {
                                            AccountGroupId = new DuplicateDiffAccountGroup { Id = "A" },
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
                TraceId = "left",
            }),
            ("b", () => new DuplicateDiffRootResponse
            {
                Applicants =
                [
                    new DuplicateDiffApplicant
                    {
                        Datasets =
                        [
                            new DuplicateDiffDataset
                            {
                                Accounts =
                                [
                                    new DuplicateDiffAccount
                                    {
                                        AccountDetails = new DuplicateDiffAccountDetails
                                        {
                                            AccountGroupId = new DuplicateDiffAccountGroup { Id = "B" },
                                        },
                                    },
                                ],
                            },
                        ],
                    },
                ],
                TraceId = "right",
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(maxDifferences: 2)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(2, result.Differences.Count);
        Assert.IsTrue(
            result.Differences.Any(difference => difference.PropertyPath.Contains("AccountGroupId.Id", StringComparison.Ordinal)),
            string.Join(" | ", result.Differences.Select(difference => difference.PropertyPath)));
        Assert.IsTrue(
            result.Differences.Any(difference => difference.PropertyPath.Equals("TraceId", StringComparison.Ordinal)),
            string.Join(" | ", result.Differences.Select(difference => difference.PropertyPath)));
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreRuleTargetsInterfaceCollection_SuppressesCollectionChildDifferences()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new InterfaceCollectionResponse
            {
                Items = new List<ManyIgnoredItem> { new ManyIgnoredItem { Ignored = "a-1", Shared = 1 } },
                Visible = "left",
            }),
            ("b", () => new InterfaceCollectionResponse
            {
                Items = new List<ManyIgnoredItem> { new ManyIgnoredItem { Ignored = "b-1", Shared = 1 } },
                Visible = "right",
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Items[*].Ignored") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(1, result.Differences.Count);
        Assert.AreEqual("Visible", result.Differences.Single().PropertyPath);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreRuleTargetsCollectionParent_SuppressesChildDifferences()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ManyIgnoredResponse
            {
                Items = new List<ManyIgnoredItem>
                {
                    new ManyIgnoredItem { Ignored = "a-1", Shared = 1 },
                    new ManyIgnoredItem { Ignored = "a-2", Shared = 2 },
                },
                Visible = "left",
            }),
            ("b", () => new ManyIgnoredResponse
            {
                Items = new List<ManyIgnoredItem>
                {
                    new ManyIgnoredItem { Ignored = "b-1", Shared = 11 },
                    new ManyIgnoredItem { Ignored = "b-2", Shared = 22 },
                },
                Visible = "right",
            }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Items") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(1, result.Differences.Count);
        Assert.AreEqual("Visible", result.Differences.Single().PropertyPath);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreRuleIncludesExpectedPrefix_SuppressesMatchingDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new ComplexResponse { Detail = new ComplexDetail { Ignored = "ignore-a", Remaining = "left" } }),
            ("b", () => new ComplexResponse { Detail = new ComplexDetail { Ignored = "ignore-b", Remaining = "right" } }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Expected.Detail.Ignored") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.AreEqual(1, result.Differences.Count);
        Assert.AreEqual("Detail.Remaining", result.Differences.Single().PropertyPath);
    }

    [TestMethod]
    public async Task CompareAsync_WhenConcurrentRunsUseDifferentOptions_ProducesIsolatedResults()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "alpha" }));

        Task<RequestPairResult> ignoredCaseTask = comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreStringCase: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);
        Task<RequestPairResult> caseSensitiveTask = comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        RequestPairResult[] results = await Task.WhenAll(ignoredCaseTask, caseSensitiveTask);

        Assert.AreEqual(RequestPairOutcome.Equal, results[0].Outcome);
        Assert.AreEqual(RequestPairOutcome.Different, results[1].Outcome);
    }

    private static CompareNetObjectsResponseComparer CreateComparer(
        params (string ArtifactId, Func<object> Factory)[] models)
    {
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore(models.Select(model => model.ArtifactId));
        FakeResponseBodyDeserializer deserializer = new FakeResponseBodyDeserializer(models);
        return new CompareNetObjectsResponseComparer(artifactStore, deserializer);
    }

    private static RequestItem CreateRequest() => new RequestItem("one.json", "application/json", 2);

    private static RunOptions CreateOptions(ComparisonOptions? comparisonOptions = null) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            "Sample",
            comparisonOptions);

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId)
    {
        byte[] content = Encoding.UTF8.GetBytes(artifactId);

        return new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
            200,
            "application/json",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    private sealed class InMemoryArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> contentByArtifactId;

        public InMemoryArtifactStore(IEnumerable<string> artifactIds)
        {
            contentByArtifactId = artifactIds.ToDictionary(
                artifactId => artifactId,
                artifactId => Encoding.UTF8.GetBytes(artifactId),
                StringComparer.Ordinal);
        }

        public Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            ArtifactReference artifact,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(contentByArtifactId[artifact.ArtifactId]));
    }

    private sealed class FakeResponseBodyDeserializer : IResponseBodyDeserializer
    {
        private readonly Dictionary<string, Func<object>> models;

        public FakeResponseBodyDeserializer(IEnumerable<(string ArtifactId, Func<object> Factory)> models)
        {
            this.models = models.ToDictionary(model => model.ArtifactId, model => model.Factory, StringComparer.Ordinal);
        }

        public async Task<object> DeserializeAsync(
            string modelName,
            Stream body,
            string? contentType,
            ComparisonOptions comparisonOptions,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new StreamReader(body, Encoding.UTF8);
            string artifactId = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return models[artifactId]();
        }
    }

    public sealed class ReportResponseModel
    {
        [JsonPropertyName("details")]
        public ReportDetails? Details { get; set; }

        [JsonPropertyName("apps")]
        public Applicant[]? Applicants { get; set; }

        [JsonIgnore]
        public bool IsAThing { get; set; }
    }

    public sealed class ReportDetails
    {
        public string? ReportId { get; set; }
    }

    public sealed class Applicant
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int Score { get; set; }

        public ApplicantAddress? Address { get; set; }
    }

    public sealed class ApplicantAddress
    {
        public string? Line1 { get; set; }

        public string? Postcode { get; set; }
    }

    public sealed class SampleResponse
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? CorrelationId { get; init; }

        public List<int>? Values { get; init; }
    }

    public sealed class MixedCollectionResponse
    {
        public List<int>? Numbers { get; init; }

        public string[]? Codes { get; init; }

        public List<Applicant>? Applicants { get; init; }

        public HashSet<string>? Tags { get; init; }
    }

    public sealed class ComplexResponse
    {
        public ComplexDetail Detail { get; init; } = new ComplexDetail();
    }

    public sealed class ComplexDetail
    {
        public string? Ignored { get; init; }

        public string? Remaining { get; init; }
    }

    public sealed class InterfaceCollectionResponse
    {
        public IList<ManyIgnoredItem> Items { get; init; } = new List<ManyIgnoredItem>();

        public string? Visible { get; init; }
    }

    public sealed class ManyIgnoredResponse
    {
        public List<ManyIgnoredItem> Items { get; init; } = new List<ManyIgnoredItem>();

        public string? Visible { get; init; }
    }

    public sealed class ManyIgnoredItem
    {
        public string? Ignored { get; init; }

        public int Shared { get; init; }
    }

    public sealed class DuplicateDiffRootResponse
    {
        public DuplicateDiffApplicant[] Applicants { get; init; } = Array.Empty<DuplicateDiffApplicant>();

        public string? TraceId { get; init; }
    }

    public sealed class DuplicateDiffApplicant
    {
        public IList<DuplicateDiffDataset> Datasets { get; init; } = new List<DuplicateDiffDataset>();
    }

    public sealed class DuplicateDiffDataset
    {
        public IList<DuplicateDiffAccount> Accounts { get; init; } = new List<DuplicateDiffAccount>();
    }

    public sealed class DuplicateDiffAccount
    {
        public DuplicateDiffAccountDetails AccountDetails { get; init; } = new DuplicateDiffAccountDetails();
    }

    public sealed class DuplicateDiffAccountDetails
    {
        public DuplicateDiffAccountGroup AccountGroupId { get; init; } = new DuplicateDiffAccountGroup();
    }

    public sealed class DuplicateDiffAccountGroup
    {
        public string? Id { get; init; }
    }
}

