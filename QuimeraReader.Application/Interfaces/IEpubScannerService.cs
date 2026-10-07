using System.Threading.Tasks;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Interfaces;

public interface IEpubScannerService
{
    Task<Book> ScanEpubAsync(string epubPath, string fallbackMetadataProvider = "GoogleBooks", string? originalFileName = null, bool forceMove = false);
}
