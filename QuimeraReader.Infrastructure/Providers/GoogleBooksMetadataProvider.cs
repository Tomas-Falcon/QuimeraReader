using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Providers;

public class GoogleBooksMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderName => "GoogleBooks";

    public GoogleBooksMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, string? isbn = null, Dictionary<string, string>? settings = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("GoogleBooksApiKey", out apiKey);

        if (string.IsNullOrWhiteSpace(apiKey)) 
            return null;

        string search = isbn != null ? $"isbn:{isbn}" : $"intitle:{query}";
        string url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(search)}&key={apiKey}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url);
            var firstItem = response?.Items?.FirstOrDefault()?.VolumeInfo;

            if (firstItem == null) return null;

            return new BookMetadata
            {
                Title = firstItem.Title,
                Authors = firstItem.Authors ?? new List<string>(),
                CoverImageUri = firstItem.ImageLinks?.Thumbnail,
                Categories = firstItem.Categories ?? new List<string>(),
                Synopsis = firstItem.Description
            };
        }
        catch
        {
            return null;
        }
    }

    internal class GoogleBooksResponse { public List<GoogleBooksItem>? Items { get; set; } }
    internal class GoogleBooksItem { public VolumeInfo? VolumeInfo { get; set; } }
    internal class VolumeInfo { 
        public string? Title { get; set; } 
        public string? Description { get; set; }
        public List<string>? Authors { get; set; }
        public List<string>? Categories { get; set; }
        public ImageLinks? ImageLinks { get; set; }
    }
    internal class ImageLinks { public string? Thumbnail { get; set; } }
}
