using ComparisonTool.Core.Abstractions;
using System.Threading.Tasks;

namespace ComparisonTool.Web.Services
{
    public class WebFileExportService : IFileExportService {
        public Task<bool> ExportAsync(string filename, string content, string contentType) => Task.FromResult(true);
    }
}
