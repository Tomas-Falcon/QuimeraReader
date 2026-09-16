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

    public async Task<BookMetadata?> GetMetadataAsync(string query, string? isbn = null, Dictionary<string, string>? settings = null)
    {
        string search = isbn != null ? $"isbn:{isbn}" : $"title={Uri.EscapeDataString(query)}";
        string url = $"https://openlibrary.org/search.json?{search}&limit=1";

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
