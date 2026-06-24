using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.Serialization.BlazorReport;
using Microsoft.AspNetCore.Components;

namespace ComparisonTool.Web.Services
{
    // todo: breaking solid here and we also have magic strings in the controller. We should probably have a shared constant for this.
    public class WebRequestComparisonGateway : IRequestComparisonGateway
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly NavigationManager _navigationManager;

        public WebRequestComparisonGateway(IHttpClientFactory httpClientFactory, NavigationManager navigationManager)
        {
            _httpClientFactory = httpClientFactory;
            _navigationManager = navigationManager;
        }

        public async Task<RequestBatchResult> StateRequestStreamsAsync(IEnumerable<(string FileName, Stream Content)> files, string? cacheKey = null)
        {
            using var httpClient = CreateHttpClient();
            using var content = new MultipartFormDataContent();

            if(!string.IsNullOrEmpty(cacheKey))
            {
                content.Add(new StringContent(cacheKey), "cacheKey");
            }

            foreach(var file in files)
            {
                var streamContent = new StreamContent(file.Content);
                content.Add(streamContent, "files", file.FileName);
            }

            var response = await httpClient.PostAsync("api/requests/batch", content);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RequestBatchUploadResponse>();

            return new RequestBatchResult(result?.BatchId ?? string.Empty, result?.Uploaded ?? 0, result?.CacheHit ?? false);
        }

        public Task<RequestBatchResult> StageRequestFilesAsync(IReadOnlyList<string> filePaths, string? cacheKey = null) => throw new NotImplementedException();

        public async Task<string> StartComparisonAsync(CreateRequestComparisonJobRequest request, CancellationToken cancellationToken = default)
        {
            using var httpClient = CreateHttpClient();
            var response = await httpClient.PostAsJsonAsync("api/requests/compare", request, cancellationToken);

            if(!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"Failed to start comparison job: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<CreateJobResponse>(cancellationToken: cancellationToken);

            return result?.JobId ?? throw new Exception("Failed to parse job ID from response.");
        }

        public async Task<RequestJobStatus> GetJobStatusAsync(string jobId)
        {
            using var httpClient = CreateHttpClient();
            var response = await httpClient.GetFromJsonAsync<JobStatusResponse>($"api/requests/compare/{jobId}/status");

            return response == null
                ? new RequestJobStatus("Unknown", 0, 0, "Failed to retrieve job status.", null)
                : new RequestJobStatus(response.Status, response.Completed, response.Total, response.Message, response.Error);
        }

        public async Task<MultiFolderComparisonResult?> GetJobResultAsync(string jobId)
        {
            using var httpClient = CreateHttpClient();
            using var response = await httpClient.GetAsync($"api/requests/compare/{jobId}/result");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<MultiFolderComparisonResult?>(
                stream,
                BlazorReportSerializerOptions.Default);
        }

        public async Task CancelJobAsync(string jobId)
        {
            using var httpClient = CreateHttpClient();
            await httpClient.PostAsync($"api/requests/compare/{jobId}/cancel", null);
        }

        private HttpClient CreateHttpClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(_navigationManager.BaseUri);
            return client;
        }
    }
}
