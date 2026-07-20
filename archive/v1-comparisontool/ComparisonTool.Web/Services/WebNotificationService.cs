using ComparisonTool.Core.Abstractions;
using System.Threading.Tasks;

namespace ComparisonTool.Web.Services
{
    public class WebNotificationService : INotificationService {
        public Task ShowSuccessAsync(string message) => Task.CompletedTask;
        public Task ShowErrorAsync(string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string message) => Task.CompletedTask;
    }
}
