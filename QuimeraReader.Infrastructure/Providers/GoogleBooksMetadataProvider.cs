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

        // Intento 1: Por ISBN si existe
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            var result = await FetchFromGoogleBooks($"isbn:{isbn}", apiKey);
            if (result != null) return result;
        }

        // Intento 2: Por Título (Fallback o si no había ISBN)
        return await FetchFromGoogleBooks($"intitle:{query}", apiKey);
    }

    private async Task<BookMetadata?> FetchFromGoogleBooks(string searchString, string? apiKey)
    {
        string url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(searchString)}";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            url += $"&key={apiKey}";
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url);
            var firstItem = response?.Items?.FirstOrDefault()?.VolumeInfo;

            if (firstItem == null) return null;

            return new BookMetadata
            {
                Title = firstItem.Title,
                Authors = firstItem.Authors ?? new List<string>(),
                CoverImageUri = firstItem.ImageLinks?.Thumbnail?.Replace("http:", "https:"),
                Categories = firstItem.Categories ?? new List<string>(),
                Synopsis = firstItem.Description,
                AverageRating = firstItem.AverageRating
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
        public double? AverageRating { get; set; }
    }
    internal class ImageLinks { public string? Thumbnail { get; set; } }
}
