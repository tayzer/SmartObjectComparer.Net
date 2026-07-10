using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemWorkspaceTests
{
    [TestMethod]
    public async Task StageDirectory_WhenDirectoryContainsEligibleFiles_PreservesSortedRelativePaths()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "z.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "nested", "a.xml"), "<a />");
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageDirectoryAsync(sourceRoot, new RequestBatchReference("batch-1"));

        CollectionAssert.AreEqual(
            new[] { "nested/a.xml", "z.json" },
            manifest.Requests.Select(request => request.RelativePath).ToArray());
    }


    [TestMethod]
    public async Task StageDirectory_WhenManyFilesAreStaged_CopiesFilesAndPreservesManifestOrder()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        for (int index = 75; index >= 1; index--)
        {
            string relativePath = index % 2 == 0
                ? Path.Combine("nested", $"request-{index:000}.json")
                : $"request-{index:000}.xml";
            string sourcePath = Path.Combine(sourceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath) ?? sourceRoot);
            await File.WriteAllTextAsync(sourcePath, $"payload-{index:000}");
        }

        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageDirectoryAsync(sourceRoot, new RequestBatchReference("batch-1"));

        string[] expectedRelativePaths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CollectionAssert.AreEqual(expectedRelativePaths, manifest.Requests.Select(request => request.RelativePath).ToArray());

        foreach (RequestItem request in manifest.Requests)
        {
            string stagedPath = Path.Combine(workspaceRoot, "request-batches", "batch-1", "requests", request.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(stagedPath), $"Missing staged file {request.RelativePath}");
            Assert.AreEqual(await File.ReadAllTextAsync(Path.Combine(sourceRoot, request.RelativePath.Replace('/', Path.DirectorySeparatorChar))), await File.ReadAllTextAsync(stagedPath));
        }
    }

    [TestMethod]
    public async Task StageDirectory_WhenDirectoryContainsSidecarsAndUnderscoreFiles_ExcludesNonRequests()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json.headers.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "_skip.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "skip.bin"), "binary");
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageDirectoryAsync(sourceRoot, new RequestBatchReference("batch-1"));

        Assert.AreEqual(1, manifest.Requests.Count);
        Assert.AreEqual("one.json", manifest.Requests[0].RelativePath);
    }

    [TestMethod]
    public async Task StageFiles_WhenExplicitFilesAreProvided_StagesOnlySelectedEligibleFiles()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        string selectedJson = Path.Combine(sourceRoot, "one.json");
        string selectedXml = Path.Combine(sourceRoot, "nested", "two.xml");
        await File.WriteAllTextAsync(selectedJson, "{}");
        await File.WriteAllTextAsync(selectedXml, "<two />");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "unselected.json"), "{}");
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageFilesAsync(
            sourceRoot,
            new[] { selectedXml, selectedJson },
            new RequestBatchReference("batch-1"));

        CollectionAssert.AreEqual(
            new[] { "nested/two.xml", "one.json" },
            manifest.Requests.Select(request => request.RelativePath).ToArray());
    }
    [TestMethod]
    public async Task SaveResponseArtifact_WhenStreamIsSaved_WritesContentAndReturnsHashMetadata()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunArtifactStore store = new FileSystemRunArtifactStore(workspaceRoot);
        byte[] content = Encoding.UTF8.GetBytes("hello");
        using MemoryStream stream = new MemoryStream(content);

        ResponseArtifactMetadata metadata = await store.SaveResponseAsync(
            new RunId("run-1"),
            EndpointSlot.A,
            new RequestItem("one.json", "application/json", content.Length),
            200,
            "application/json",
            stream);

        string artifactPath = Path.Combine(workspaceRoot, metadata.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(artifactPath));
        Assert.AreEqual("hello", await File.ReadAllTextAsync(artifactPath));
        Assert.AreEqual(content.Length, metadata.ContentLength);
        Assert.AreEqual(ToSha256(content), metadata.Sha256);
    }

    [TestMethod]
    public async Task OpenReadAsync_WhenArtifactExists_ReturnsSavedContent()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunArtifactStore store = new FileSystemRunArtifactStore(workspaceRoot);
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        ResponseArtifactMetadata metadata = await store.SaveResponseAsync(
            new RunId("run-1"),
            EndpointSlot.A,
            new RequestItem("one.json", "application/json", 5),
            200,
            "application/json",
            stream);

        await using Stream loadedStream = await store.OpenReadAsync(metadata.Artifact);
        using StreamReader reader = new StreamReader(loadedStream, Encoding.UTF8);
        string loadedContent = await reader.ReadToEndAsync();

        Assert.AreEqual("hello", loadedContent);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenDetailsAreSaved_CanLoadDetailIndexWithoutRawBodies()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.json",
                RequestPairOutcome.Different,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.json"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.json"),
                areEqual: false,
                differenceCount: 1,
                differences: new[] { new ComparisonDifference("Name", "A", "B", "Changed") }),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(1, loadedDetails.Count);
        Assert.AreEqual(RequestPairOutcome.Different, loadedDetails[0].Outcome);
        Assert.AreEqual("runs/run-1/artifacts/A/one.json", loadedDetails[0].ResponseA?.Artifact.ArtifactId);
        Assert.AreEqual(1, loadedDetails[0].DifferenceCount);
        Assert.AreEqual("Name", loadedDetails[0].Differences[0].PropertyPath);
        Assert.AreEqual("A", loadedDetails[0].Differences[0].ValueA);
        Assert.AreEqual("B", loadedDetails[0].Differences[0].ValueB);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenOutcomeMessageExists_CanLoadIt()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.json",
                RequestPairOutcome.StatusCodeMismatch,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.json"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.json"),
                outcomeMessage: "Endpoint A returned 200 and endpoint B returned 500."),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual("Endpoint A returned 200 and endpoint B returned 500.", loadedDetails[0].OutcomeMessage);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenRawTextDifferencesExist_CanLoadThem()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.txt",
                RequestPairOutcome.BothNonSuccess,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.txt"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.txt"),
                differenceCount: 1,
                differences: new[] { new ComparisonDifference("Body.Line[1]", "first", "second", "Raw line differs.") }),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, loadedDetails[0].Outcome);
        Assert.AreEqual("Body.Line[1]", loadedDetails[0].Differences[0].PropertyPath);
        Assert.AreEqual("Raw line differs.", loadedDetails[0].Differences[0].Message);
    }
    [TestMethod]
    public async Task SaveRunDetails_WhenManyDetailsAreSaved_WritesLoadableDetailIndex()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = Enumerable
            .Range(1, 5000)
            .Select(index => new RequestPairResult(
                $"request-{index}.json",
                RequestPairOutcome.Equal,
                CreateResponse(EndpointSlot.A, $"runs/run-1/artifacts/A/request-{index}.json"),
                CreateResponse(EndpointSlot.B, $"runs/run-1/artifacts/B/request-{index}.json")))
            .ToArray();

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(5000, loadedDetails.Count);
        Assert.AreEqual("request-1.json", loadedDetails[0].RelativePath);
        Assert.AreEqual("request-5000.json", loadedDetails[^1].RelativePath);
    }
    [TestMethod]
    public async Task SaveRun_WhenRunIsSaved_CanLoadRunAndSummary()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 1,
            equalPairs: 1,
            differentPairs: 0,
            errorPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/index.json"),
            executionMetrics: new RunExecutionMetrics(
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3),
                requestCount: 1,
                maxConcurrency: 2,
                responseBytesWritten: 4));
        ComparisonRun run = ComparisonRun
            .Create(new RunId("run-1"), CreateOptions())
            .Start()
            .Complete(summary);

        await store.SaveAsync(run);
        ComparisonRun? loadedRun = await store.LoadAsync(run.Id);
        RunResultSummary? loadedSummary = await store.LoadSummaryAsync(run.Id);

        Assert.IsNotNull(loadedRun);
        Assert.AreEqual(RunStatus.Completed, loadedRun.Status);
        Assert.IsNotNull(loadedSummary);
        Assert.AreEqual(1, loadedSummary.EqualPairs);
        Assert.AreEqual("runs/run-1/details/index.json", loadedSummary.DetailIndexReference?.DetailId);
        Assert.AreEqual(1, loadedSummary.ExecutionMetrics?.RequestCount);
        Assert.AreEqual(4, loadedSummary.ExecutionMetrics?.ResponseBytesWritten);
    }

    [TestMethod]
    public async Task SaveRun_WhenRunIncludesOptions_PersistsAndLoadsOptions()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            ignoreCollectionOrder: true,
            ignoreStringCase: true,
            ignoreTrailingWhitespaceAtEnd: true,
            treatNullAndEmptyCollectionsAsEqual: true,
            ignoreXmlNamespaces: false,
            maxDifferences: 12,
            ignoreRules: new[] { new IgnoreRuleDefinition("Name") },
            smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Id") },
            maskRules: new[] { new MaskRuleDefinition("Token", 4, "#") });
        RunOptions options = CreateOptions(comparisonOptions, new RequestExecutionOptions("application/xml"), new ContractProfileSelection("profile-a"));
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), options);

        await store.SaveAsync(run);
        ComparisonRun? loadedRun = await store.LoadAsync(run.Id);

        Assert.IsNotNull(loadedRun);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreCollectionOrder);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreStringCase);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreTrailingWhitespaceAtEnd);
        Assert.IsTrue(loadedRun.Options.Comparison.TreatNullAndEmptyCollectionsAsEqual);
        Assert.IsFalse(loadedRun.Options.Comparison.IgnoreXmlNamespaces);
        Assert.AreEqual(12, loadedRun.Options.Comparison.MaxDifferences);
        Assert.AreEqual("Name", loadedRun.Options.Comparison.IgnoreRules[0].PropertyPath);
        Assert.AreEqual("Id", loadedRun.Options.Comparison.SmartIgnoreRules[0].Value);
        Assert.AreEqual("Token", loadedRun.Options.Comparison.MaskRules[0].PropertyPath);
        Assert.AreEqual("application/xml", loadedRun.Options.RequestExecution.ContentTypeOverride);
        Assert.AreEqual("profile-a", loadedRun.Options.ContractProfile?.ProfileId);
    }

    [TestMethod]
    public async Task SaveRun_WhenExistingSnapshotIsOpenForRead_ReplacesSnapshot()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        RunId runId = new RunId("run-1");
        ComparisonRun createdRun = ComparisonRun.Create(runId, CreateOptions());
        await store.SaveAsync(createdRun);
        string runPath = Path.Combine(workspaceRoot, "runs", runId.Value, "run.json");

        await using FileStream reader = new FileStream(
            runPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);

        ComparisonRun executingRun = createdRun
            .Start()
            .Advance(RunStatus.Executing, new RunProgress(50, "Executing."));
        await store.SaveAsync(executingRun);

        ComparisonRun? loadedRun = await store.LoadAsync(runId);

        Assert.IsNotNull(loadedRun);
        Assert.AreEqual(RunStatus.Executing, loadedRun.Status);
        Assert.AreEqual(50, loadedRun.Progress.PercentComplete);
    }

    [TestMethod]
    public async Task LoadRun_WhenSnapshotIsOpenForSharedWrite_ReturnsRun()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        RunId runId = new RunId("run-1");
        ComparisonRun run = ComparisonRun.Create(runId, CreateOptions());
        await store.SaveAsync(run);
        string runPath = Path.Combine(workspaceRoot, "runs", runId.Value, "run.json");

        await using FileStream writer = new FileStream(
            runPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);

        ComparisonRun? loadedRun = await store.LoadAsync(runId);

        Assert.IsNotNull(loadedRun);
        Assert.AreEqual(runId.Value, loadedRun.Id.Value);
        Assert.AreEqual(RunStatus.Created, loadedRun.Status);
    }

        [TestMethod]
        public async Task LoadRun_WhenLegacySnapshotOmitsRetentionFields_UsesBackwardCompatibleDefaults()
        {
                string workspaceRoot = CreateTempDirectory();
                string runPath = Path.Combine(workspaceRoot, "runs", "legacy-run", "run.json");
                Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
                await File.WriteAllTextAsync(
                        runPath,
                        """
                        {
                            "Id": "legacy-run",
                            "Options": {
                                "RequestBatch": "batch-1",
                                "EndpointA": { "Uri": "https://service-a.example.test", "Headers": {} },
                                "EndpointB": { "Uri": "https://service-b.example.test", "Headers": {} },
                                "TimeoutMilliseconds": 30000,
                                "MaxConcurrency": 2,
                                "ResponseModelName": "Auto",
                                "ModelName": "Auto",
                                "Comparison": { "IgnoreRules": [], "SmartIgnoreRules": [], "MaskRules": [] },
                                "RequestExecution": {},
                                "LargeRun": {}
                            },
                            "Status": "Created",
                            "Progress": { "PercentComplete": 0, "Message": "Run created." },
                            "CreatedAt": "2026-01-01T00:00:00Z",
                            "UpdatedAt": "2026-01-01T00:00:00Z"
                        }
                        """);

                FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);

                ComparisonRun? loaded = await store.LoadAsync(new RunId("legacy-run"));

                Assert.IsNotNull(loaded);
                Assert.AreEqual(RetentionMode.TrimmedEqualsAndIgnoredPaths, loaded.RunRetentionMode);
                Assert.AreEqual("v1", loaded.RunRetentionPolicyVersion);
                Assert.IsNull(loaded.ComparisonRulesSnapshotHash);
                Assert.IsNull(loaded.Options.RunRetentionModeOverride);
        }

        [TestMethod]
        public async Task LoadDetails_WhenLegacyPageOmitsRetentionFields_UsesBackwardCompatibleDefaults()
        {
                string workspaceRoot = CreateTempDirectory();
                string detailsRoot = Path.Combine(workspaceRoot, "runs", "run-1", "details");
                string pagePath = Path.Combine(detailsRoot, "pages", "page-000000.json");
                string manifestPath = Path.Combine(detailsRoot, "manifest.json");
                Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
                await File.WriteAllTextAsync(
                        pagePath,
                        """
                        [
                            {
                                "RelativePath": "one.json",
                                "Outcome": "Different",
                                "ResponseA": {
                                    "Endpoint": "A",
                                    "ArtifactId": "runs/run-1/artifacts/A/one.json",
                                    "ArtifactContentType": "application/json",
                                    "StatusCode": 200,
                                    "ContentType": "application/json",
                                    "ContentLength": 2,
                                    "Sha256": "abc"
                                },
                                "ResponseB": {
                                    "Endpoint": "B",
                                    "ArtifactId": "runs/run-1/artifacts/B/one.json",
                                    "ArtifactContentType": "application/json",
                                    "StatusCode": 200,
                                    "ContentType": "application/json",
                                    "ContentLength": 2,
                                    "Sha256": "def"
                                },
                                "FocusedRawContentIgnorePaths": [],
                                "DifferenceCount": 1,
                                "Differences": [
                                    {
                                        "PropertyPath": "Name",
                                        "ValueA": "A",
                                        "ValueB": "B",
                                        "Message": "Changed"
                                    }
                                ]
                            }
                        ]
                        """);
                await File.WriteAllTextAsync(
                        manifestPath,
                        """
                        {
                            "SchemaVersion": 2,
                            "RunId": "run-1",
                            "PageSize": 250,
                            "TotalCount": 1,
                            "Pages": [
                                {
                                    "PageIndex": 0,
                                    "Offset": 0,
                                    "ItemCount": 1,
                                    "Path": "runs/run-1/details/pages/page-000000.json"
                                }
                            ]
                        }
                        """);

                FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
                RunDetailReference reference = new RunDetailReference("runs/run-1/details/manifest.json");

                IReadOnlyList<RequestPairResult> details = await store.LoadDetailsAsync(reference);

                Assert.AreEqual(1, details.Count);
                Assert.AreEqual(PairRetentionClass.Different, details[0].PairRetentionClass);
                Assert.AreEqual(ArtifactRetentionState.Retained, details[0].ArtifactRetentionState.RawResponseA);
                Assert.AreEqual(ArtifactRetentionState.Retained, details[0].ArtifactRetentionState.CanonicalResponseA);
                Assert.IsNull(details[0].RetentionAppliedAt);
        }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
            200,
            "application/json",
            2,
            "abc");

    private static RunOptions CreateOptions(
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        ContractProfileSelection? contractProfileSelection = null) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            comparisonOptions: comparisonOptions,
            requestExecutionOptions: requestExecutionOptions,
            contractProfileSelection: contractProfileSelection);

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}


