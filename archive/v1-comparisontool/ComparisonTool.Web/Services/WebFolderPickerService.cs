using ComparisonTool.Core.Abstractions;
using System.Threading.Tasks;

namespace ComparisonTool.Web.Services
{
    public class WebFolderPickerService : IFolderPickerService {
        public Task<string> PickFolderAsync(string? dialogTitle = null) => Task.FromResult(string.Empty);
    }
}
