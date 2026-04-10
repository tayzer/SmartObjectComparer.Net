using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.RequestComparison.Models;
using System;
using System.Threading.Tasks;

namespace ComparisonTool.Web.Services
{
    public class WebProgressSubscriber : IProgressSubscriber {
        public event Action<ComparisonProgressUpdate>? OnProgressUpdate;
        public bool IsConnected => true;
        public Task StartAsync() => Task.CompletedTask;
        public Task SubscribeToJobAsync(string jobId) => Task.CompletedTask;
        public Task UnsubscribeAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
