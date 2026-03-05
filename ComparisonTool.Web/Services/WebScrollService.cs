using ComparisonTool.Core.Abstractions;
using System.Threading.Tasks;

namespace ComparisonTool.Web.Services
{
    public class WebScrollService : IScrollService {
        public Task ScrollToElementAsync(string elementId) => Task.CompletedTask;
    }
}
