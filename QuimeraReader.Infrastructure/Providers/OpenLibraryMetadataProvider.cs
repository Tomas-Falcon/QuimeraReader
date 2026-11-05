using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Providers;

public class OpenLibraryMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenLibraryMetadataProvider> _logger;

    public string ProviderName => "OpenLibrary";

    public OpenLibraryMetadataProvider(HttpClient httpClient, ILogger<OpenLibraryMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CoverCandidate>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null)
    {
        var covers = new List<CoverCandidate>();
        string query = $"title={Uri.EscapeDataString(title)}";
        if (!string.IsNullOrWhiteSpace(author)) query += $"&author={Uri.EscapeDataString(author)}";

        string url = $"https://openlibrary.org/search.json?{query}&limit=15&fields={SearchFields}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OpenLibraryResponse>(url);
            if (response?.Docs != null)
            {
                foreach (var doc in response.Docs)
                {
                    if (!doc.CoverI.HasValue) continue;
                    covers.Add(new CoverCandidate
                    {
                        ImageUrl = $"https://covers.openlibrary.org/b/id/{doc.CoverI.Value}-L.jpg",
                        Provider = ProviderName,
                        Title = doc.Title,
                        Authors = doc.AuthorName ?? new List<string>(),
                        Categories = doc.Subject?.Take(5).ToList() ?? new List<string>(),
                        Rating = doc.RatingsAverage,
                        RatingsCount = doc.RatingsCount
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenLibrary: error buscando covers para '{Title}'", title);
        }
        return covers;
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
    {
        if (isbns != null && isbns.Any())
        {
            foreach (var isbn in isbns)
            {
                if (string.IsNullOrWhiteSpace(isbn)) continue;
                var result = await FetchFromOpenLibrary($"isbn:{isbn}");
                if (result != null) return result;
            }
        }

        if (!string.IsNullOrWhiteSpace(authorHint))
        {
            var resultWithAuthor = await FetchFromOpenLibrary($"title={Uri.EscapeDataString(query)}&author={Uri.EscapeDataString(authorHint)}");
            if (resultWithAuthor != null) return resultWithAuthor;
        }

        return await FetchFromOpenLibrary($"title={Uri.EscapeDataString(query)}");
    }

    private async Task<BookMetadata?> FetchFromOpenLibrary(string queryParams)
    {
        string url = $"https://openlibrary.org/search.json?{queryParams}&limit=1&fields={SearchFields}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OpenLibraryResponse>(url);
            var firstDoc = response?.Docs?.FirstOrDefault();

            if (firstDoc == null) return null;

            string? coverImageUri = null;
            if (firstDoc.CoverI.HasValue)
            {
                coverImageUri = $"https://covers.openlibrary.org/b/id/{firstDoc.CoverI.Value}-L.jpg";
            }

            return new BookMetadata
            {
                Title = firstDoc.Title,
                Authors = firstDoc.AuthorName ?? new List<string>(),
                CoverImageUri = coverImageUri,
                Categories = firstDoc.Subject ?? new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenLibrary: error buscando '{Query}'", queryParams);
            return null;
        }
    }

    internal class OpenLibraryResponse { public List<OpenLibraryDoc>? Docs { get; set; } }
    private const string SearchFields = "title,author_name,cover_i,subject,ratings_average,ratings_count";

    // OpenLibrary responde en snake_case: sin estos nombres AuthorName y CoverI llegaban siempre vacíos
    internal class OpenLibraryDoc {
        public string? Title { get; set; }
        [JsonPropertyName("author_name")] public List<string>? AuthorName { get; set; }
        [JsonPropertyName("cover_i")] public int? CoverI { get; set; }
        public List<string>? Subject { get; set; }
        [JsonPropertyName("ratings_average")] public double? RatingsAverage { get; set; }
        [JsonPropertyName("ratings_count")] public int? RatingsCount { get; set; }
    }
}
