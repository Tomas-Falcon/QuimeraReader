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
    public double? AverageRating { get; set; }
}

/// <summary>Un resultado de la búsqueda de portadas: la imagen y los datos del libro al que pertenece.</summary>
public class CoverCandidate
{
    public string ImageUrl { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<string> Authors { get; set; } = new();
    public string? Synopsis { get; set; }
    public List<string> Categories { get; set; } = new();
    public double? Rating { get; set; }
    public int? RatingsCount { get; set; }
}

public interface IMetadataProvider
{
    string ProviderName { get; }
    Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null);
    /// <summary>Búsqueda aproximada por título y autor (no exacta), con los datos de cada resultado.</summary>
    Task<List<CoverCandidate>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null);
}
