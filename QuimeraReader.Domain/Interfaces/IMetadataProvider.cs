using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuimeraReader.Domain.Interfaces;

public class BookMetadata
{
    public string? Title { get; set; }
    public List<string> Authors { get; set; } = new();
    public string? CoverImageUri { get; set; }
    public string? Synopsis { get; set; }
    public List<string> Categories { get; set; } = new();
    public string? SeriesName { get; set; }
}

public interface IMetadataProvider
{
    string ProviderName { get; }
    Task<BookMetadata?> GetMetadataAsync(string query, string? isbn = null, Dictionary<string, string>? settings = null);
}
