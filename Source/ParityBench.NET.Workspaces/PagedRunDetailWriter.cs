using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class PagedRunDetailWriter : IRunDetailWriter
{
    private readonly FileSystemRunDetailStore owner;
    private readonly RunId runId;
    private readonly string detailRootId;
    private readonly string detailRootPath;
    private readonly int pageSize;
    private readonly List<RequestPairResult> pageBuffer;
    private readonly IncrementalRunAnalysisBuilder analysisBuilder = new IncrementalRunAnalysisBuilder();
    private readonly StaticReportDifferenceIndexAccumulator differenceIndexBuilder = new StaticReportDifferenceIndexAccumulator();
    private readonly List<DetailPageInfoDto> pages = new List<DetailPageInfoDto>();
    private bool completed;
    private int totalCount;
    private int pageIndex;

    public PagedRunDetailWriter(
        FileSystemRunDetailStore owner,
        RunId runId,
        string detailRootId,
        string detailRootPath,
        int pageSize)
    {
        this.owner = owner;
        this.runId = runId;
        this.detailRootId = detailRootId;
        this.detailRootPath = detailRootPath;
        this.pageSize = pageSize;
        pageBuffer = new List<RequestPairResult>(pageSize);
    }

    public async Task AppendAsync(
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            throw new InvalidOperationException("Cannot append run details after completion.");
        }

        foreach (RequestPairResult result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageBuffer.Add(result);
            analysisBuilder.Add(result);
            differenceIndexBuilder.Add(result);
            if (pageBuffer.Count >= pageSize)
            {
                await FlushPageAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<RunDetailReference> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            throw new InvalidOperationException("Run detail writer has already completed.");
        }

        if (pageBuffer.Count > 0)
        {
            await FlushPageAsync(cancellationToken).ConfigureAwait(false);
        }

        string differenceIndexId = FileSystemWorkspacePaths.ToLogicalPath(detailRootId, "difference-index.json");
        string analysisId = FileSystemWorkspacePaths.ToLogicalPath(detailRootId, "analysis.json");
        StaticReportDifferenceIndex differenceIndex = differenceIndexBuilder.Build();
        StaticReportAnalysisSnapshot analysis = analysisBuilder.Build(differenceIndexId);
        await owner.WriteJsonAsync(FileSystemWorkspacePaths.GetSafePath(owner.workspaceRoot, differenceIndexId), differenceIndex, cancellationToken).ConfigureAwait(false);
        await owner.WriteJsonAsync(FileSystemWorkspacePaths.GetSafePath(owner.workspaceRoot, analysisId), analysis, cancellationToken).ConfigureAwait(false);

        string manifestId = FileSystemWorkspacePaths.ToLogicalPath(detailRootId, "manifest.json");
        DetailManifestDto manifest = new DetailManifestDto
        {
            SchemaVersion = FileSystemRunDetailStore.CurrentSchemaVersion,
            RunId = runId.Value,
            PageSize = pageSize,
            TotalCount = totalCount,
            Pages = pages,
            AnalysisPath = analysisId,
            DifferenceIndexPath = differenceIndexId,
        };
        await owner.WriteJsonAsync(FileSystemWorkspacePaths.GetSafePath(owner.workspaceRoot, manifestId), manifest, cancellationToken).ConfigureAwait(false);
        completed = true;

        ArtifactReference manifestArtifact = new ArtifactReference(manifestId, "application/json");
        return new RunDetailReference(
            manifestId,
            manifestArtifact,
            FileSystemRunDetailStore.CurrentSchemaVersion,
            pageSize,
            totalCount,
            new ArtifactReference(analysisId, "application/json"),
            new ArtifactReference(differenceIndexId, "application/json"));
    }

    public async ValueTask DisposeAsync()
    {
        if (!completed && pageBuffer.Count > 0)
        {
            await FlushPageAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FlushPageAsync(CancellationToken cancellationToken)
    {
        int offset = totalCount;
        string pageId = FileSystemWorkspacePaths.ToLogicalPath(detailRootId, "pages", $"page-{pageIndex:D6}.json");
        string pagePath = FileSystemWorkspacePaths.GetSafePath(owner.workspaceRoot, pageId);
        List<RequestPairResultDto> dtos = pageBuffer.Select(owner.ToDto).ToList();
        await owner.WriteJsonAsync(pagePath, dtos, cancellationToken).ConfigureAwait(false);
        pages.Add(new DetailPageInfoDto
        {
            PageIndex = pageIndex,
            Offset = offset,
            ItemCount = dtos.Count,
            Path = pageId,
        });
        totalCount += dtos.Count;
        pageIndex++;
        pageBuffer.Clear();
    }
}
