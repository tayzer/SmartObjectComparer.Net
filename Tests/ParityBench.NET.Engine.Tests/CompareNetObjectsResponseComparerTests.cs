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
    public async Task CompareAsync_AfterReversiblePreparation_RestoresOriginalModels()
    {
        ReportResponseModel left = new()
        {
            Details = new ReportDetails { ReportId = "left" },
            Applicants =
            [
                new Applicant { Id = "2", Name = "Bob", Score = 20 },
                new Applicant { Id = "1", Name = "Alice", Score = 10 },
            ],
        };
        ReportResponseModel right = new()
        {
            Details = new ReportDetails { ReportId = "right" },
            Applicants =
            [
                new Applicant { Id = "1", Name = "Alice", Score = 10 },
                new Applicant { Id = "2", Name = "Bob", Score = 20 },
            ],
        };
        CompareNetObjectsResponseComparer comparer = CreateComparer(("a", () => left), ("b", () => right));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: [new IgnoreRuleDefinition("Details.ReportId")])),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
        Assert.AreEqual("left", left.Details!.ReportId);
        Assert.AreEqual("right", right.Details!.ReportId);
        CollectionAssert.AreEqual(new[] { "2", "1" }, left.Applicants!.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "2" }, right.Applicants!.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task CompareAsync_OptimizedPreparation_MatchesLegacyOrderedDifferences()
    {
        Random random = new(1977);
        for (int iteration = 0; iteration < 25; iteration++)
        {
            Applicant[] leftApplicants = Enumerable.Range(0, 12)
                .Select(index => new Applicant { Id = index.ToString(), Name = $"name-{index}", Score = random.Next(100), Address = new ApplicantAddress { Line1 = $"ignored-{random.Next()}", Postcode = $"P{index}" } })
                .ToArray();
            Applicant[] rightApplicants = leftApplicants
                .Select(item => new Applicant { Id = item.Id, Name = item.Name, Score = item.Score, Address = new ApplicantAddress { Line1 = $"ignored-{random.Next()}", Postcode = item.Address?.Postcode } })
                .OrderBy(_ => random.Next())
                .ToArray();
            rightApplicants[iteration % rightApplicants.Length].Name += "-changed";

            Func<object> leftFactory = () => new ReportResponseModel { Details = new ReportDetails { ReportId = "left" }, Applicants = CloneApplicants(leftApplicants) };
            Func<object> rightFactory = () => new ReportResponseModel { Details = new ReportDetails { ReportId = "right" }, Applicants = CloneApplicants(rightApplicants) };
            (string ArtifactId, Func<object> Factory)[] models = [("a", leftFactory), ("b", rightFactory)];
            ComparisonOptions comparison = new(
                ignoreCollectionOrder: true,
                maxDifferences: 100,
                ignoreRules: [new IgnoreRuleDefinition("Details.ReportId"), new IgnoreRuleDefinition("Applicants[*].Address.Line1")]);

            RequestPairResult optimized = await CreateComparer(models).CompareAsync(CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);
            RequestPairResult legacy = await CreateLegacyComparer(models).CompareAsync(CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);

            Assert.AreEqual(legacy.Outcome, optimized.Outcome, $"iteration {iteration}");
            CollectionAssert.AreEqual(
                legacy.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray(),
                optimized.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray(),
                $"iteration {iteration}");
        }
    }

    [TestMethod]
    public async Task CompareAsync_WithNestedArraysAndDictionaries_DoesNotFailPreparation()
    {
        BenchmarkShape left = CreateBenchmarkShape(reverse: false);
        BenchmarkShape right = CreateBenchmarkShape(reverse: true);
        CompareNetObjectsResponseComparer comparer = CreateComparer(("a", () => left), ("b", () => right));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: [new IgnoreRuleDefinition("Items[*].Amount")],
                smartIgnoreRules: [new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Description")])),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        CompareNetObjectsResponseComparer legacyComparer = CreateLegacyComparer(
            ("a", () => CreateBenchmarkShape(reverse: false)),
            ("b", () => CreateBenchmarkShape(reverse: true)));
        RequestPairResult legacy = await legacyComparer.CompareAsync(
            CreateRequest(),
            CreateOptions(new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: [new IgnoreRuleDefinition("Items[*].Amount")],
                smartIgnoreRules: [new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Description")])),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome, result.ErrorMessage);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath.Contains("Payload", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            legacy.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray(),
            result.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray());
    }

    [TestMethod]
    public async Task CompareAsync_WithUnicodeDuplicatesNullsAndReadOnlyCollections_MatchesLegacyOutput()
    {
        Func<bool, EdgeCaseShape> create = reverse => new EdgeCaseShape
        {
            Values = reverse
                ? new List<string?> { "Ω", null, "same", "é", "same" }
                : new List<string?> { "same", "é", "same", null, "Ω" },
            ReadOnlyApplicants = Array.AsReadOnly((reverse
                ? new[] { new Applicant { Id = "2", Name = "二" }, new Applicant { Id = "1", Name = "é" } }
                : new[] { new Applicant { Id = "1", Name = "é" }, new Applicant { Id = "2", Name = "二" } })),
            Lookup = new Dictionary<string, List<int>>
            {
                ["numbers"] = reverse ? new List<int> { 3, 2, 1 } : new List<int> { 1, 2, 3 },
            },
        };
        (string ArtifactId, Func<object> Factory)[] models =
        [
            ("a", () => create(false)),
            ("b", () => create(true)),
        ];
        ComparisonOptions comparison = new(ignoreCollectionOrder: true, includeAllDifferences: true);

        RequestPairResult optimized = await CreateComparer(models).CompareAsync(
            CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);
        RequestPairResult legacy = await CreateLegacyComparer(models).CompareAsync(
            CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);

        Assert.AreEqual(legacy.Outcome, optimized.Outcome);
        CollectionAssert.AreEqual(
            legacy.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray(),
            optimized.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray());
    }

    [TestMethod]
    public async Task CompareAsync_WhenPreparationThrows_RestoresEarlierMutations()
    {
        ThrowingModel left = new() { Ignored = "left" };
        ThrowingModel right = new() { Ignored = "right" };
        CompareNetObjectsResponseComparer comparer = CreateComparer(("a", () => left), ("b", () => right));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(new ComparisonOptions(ignoreRules: [new IgnoreRuleDefinition("Ignored")])),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.ExecutionFailed, result.Outcome);
        Assert.AreEqual("left", left.Ignored);
        Assert.AreEqual("right", right.Ignored);
    }

    [TestMethod]
    public async Task CompareAsync_WithCyclesAndDuplicateFallbackKeys_MatchesLegacyAndRestoresModels()
    {
        CycleNode leftFirst = new() { Id = "2" };
        CycleNode leftSecond = new() { Id = "1" };
        leftFirst.Parent = leftFirst;
        leftSecond.Parent = leftSecond;
        CycleNode rightFirst = new() { Id = "1" };
        CycleNode rightSecond = new() { Id = "2" };
        rightFirst.Parent = rightFirst;
        rightSecond.Parent = rightSecond;
        CycleShape left = new() { Nodes = [leftFirst, leftSecond] };
        CycleShape right = new() { Nodes = [rightFirst, rightSecond] };
        ComparisonOptions comparison = new(ignoreCollectionOrder: true, includeAllDifferences: true);

        RequestPairResult optimized = await CreateComparer(("a", () => left), ("b", () => right)).CompareAsync(
            CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);
        RequestPairResult legacy = await CreateLegacyComparer(("a", () => left), ("b", () => right)).CompareAsync(
            CreateRequest(), CreateOptions(comparison), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null);

        Assert.AreEqual(legacy.Outcome, optimized.Outcome);
        CollectionAssert.AreEqual(
            legacy.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray(),
            optimized.Differences.Select(difference => (difference.PropertyPath, difference.ValueA, difference.ValueB, difference.Message)).ToArray());
        CollectionAssert.AreEqual(new[] { "2", "1" }, left.Nodes!.Select(node => node.Id).ToArray());
        Assert.AreSame(leftFirst, leftFirst.Parent);
    }

    [TestMethod]
    public async Task CompareAsync_WithDetailedTiming_RecordsConsumedBytesAndKeepsDifferences()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Beta" }));
        DetailedCompareMetricsCollector collector = new();

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(), CreateOptions(), CreateResponse(EndpointSlot.A, "a"), CreateResponse(EndpointSlot.B, "b"), null, collector);
        DetailedCompareMetrics metrics = collector.ToMetrics(TimeSpan.FromSeconds(1));

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath.Contains("Name", StringComparison.Ordinal)));
        Assert.AreEqual(2, metrics.ArtifactBytesRead);
        Assert.IsTrue(metrics.ArtifactOpenDuration >= TimeSpan.Zero);
        Assert.IsTrue(metrics.ResponseDeserializationDuration >= TimeSpan.Zero);
        Assert.IsTrue(metrics.ComparisonModelNormalizationDuration >= TimeSpan.Zero);
        Assert.IsTrue(metrics.CompareNetObjectsTraversalDuration >= TimeSpan.Zero);
        Assert.IsTrue(metrics.DifferenceMaterializationDuration >= TimeSpan.Zero);
    }

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
    public async Task CompareAsync_WhenIncludeAllDifferencesEnabled_DoesNotApplyConfiguredLimit()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha", Description = "One" }),
            ("b", () => new SampleResponse { Id = 2, Name = "Beta", Description = "Two" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(maxDifferences: 1, includeAllDifferences: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.IsTrue(result.DifferenceCount >= 3);
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

    private static CompareNetObjectsResponseComparer CreateLegacyComparer(
        params (string ArtifactId, Func<object> Factory)[] models)
    {
        InMemoryArtifactStore artifactStore = new(models.Select(model => model.ArtifactId));
        FakeResponseBodyDeserializer deserializer = new(models);
        return new CompareNetObjectsResponseComparer(artifactStore, deserializer, useLegacyNormalizer: true);
    }

    private static Applicant[] CloneApplicants(IEnumerable<Applicant> applicants) => applicants
        .Select(item => new Applicant
        {
            Id = item.Id,
            Name = item.Name,
            Score = item.Score,
            Address = item.Address is null ? null : new ApplicantAddress { Line1 = item.Address.Line1, Postcode = item.Address.Postcode },
        })
        .ToArray();

    private static BenchmarkShape CreateBenchmarkShape(bool reverse)
    {
        IEnumerable<int> indexes = reverse ? Enumerable.Range(0, 1024).Reverse() : Enumerable.Range(0, 1024);
        return new BenchmarkShape
        {
            Payload = reverse ? "changed" : "stable",
            Padding = new string('x', reverse ? 4095 : 4096),
            Items = indexes.Select(index => new BenchmarkItem
            {
                Id = index,
                Amount = reverse ? index + 1 : index,
                Description = reverse ? "ignored-right" : "ignored-left",
                Tags = ["a", "b"],
                Attributes = new Dictionary<string, string> { ["source"] = "test", ["partition"] = (index % 2).ToString() },
            }).ToArray(),
        };
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

    public sealed class BenchmarkShape
    {
        public string? Payload { get; set; }
        public BenchmarkItem[]? Items { get; set; }
        public string? Padding { get; set; }
    }

    public sealed class BenchmarkItem
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string[]? Tags { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }

    public sealed class EdgeCaseShape
    {
        public List<string?>? Values { get; set; }
        public IReadOnlyList<Applicant>? ReadOnlyApplicants { get; set; }
        public Dictionary<string, List<int>>? Lookup { get; set; }
    }

    public sealed class ThrowingModel
    {
        private string? throwingValue;
        public string? Ignored { get; set; }
        public string? Throws
        {
            get => throw new InvalidOperationException("preparation failure");
            set => throwingValue = value;
        }
    }

    public sealed class CycleShape
    {
        public CycleNode[]? Nodes { get; set; }
    }

    public sealed class CycleNode
    {
        public string? Id { get; set; }
        public CycleNode? Parent { get; set; }
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

