using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Providers;

public class OpenLibraryMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderName => "OpenLibrary";

    public OpenLibraryMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null)
    {
        // Intento 1: Por ISBN si existen
        if (isbns != null && isbns.Any())
        {
            foreach (var isbn in isbns)
            {
                if (string.IsNullOrWhiteSpace(isbn)) continue;
                var result = await FetchFromOpenLibrary($"isbn:{isbn}");
                if (result != null) return result;
            }
        }

        // Intento 2: Por título (Fallback o si no hay ISBN)
        return await FetchFromOpenLibrary($"title={Uri.EscapeDataString(query)}");
    }

    private async Task<BookMetadata?> FetchFromOpenLibrary(string queryParams)
    {
        string url = $"https://openlibrary.org/search.json?{queryParams}&limit=1";

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
        catch
        {
            return null;
        }
    }

    internal class OpenLibraryResponse { public List<OpenLibraryDoc>? Docs { get; set; } }
    internal class OpenLibraryDoc { 
        public string? Title { get; set; } 
        public List<string>? AuthorName { get; set; }
        public int? CoverI { get; set; }
        public List<string>? Subject { get; set; }
    }
}
