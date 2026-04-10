using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Web.Services
{
    public class WebRequestComparisonGateway : IRequestComparisonGateway
    {
        public Task<RequestBatchResult> StageRequestFilesAsync(IReadOnlyList<string> filePaths, string? cacheKey = null) => Task.FromResult(new RequestBatchResult("dummy", 0, false));
        public Task<string> StartComparisonAsync(CreateRequestComparisonJobRequest request, CancellationToken cancellationToken = default) => Task.FromResult("dummy");
        public Task<RequestJobStatus> GetJobStatusAsync(string jobId) => Task.FromResult(new RequestJobStatus("dummy", 0, 0, null, null));
        public Task<MultiFolderComparisonResult?> GetJobResultAsync(string jobId) => Task.FromResult<MultiFolderComparisonResult?>(null);
        public Task CancelJobAsync(string jobId) => Task.CompletedTask;
    }
}
