using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Providers;

public class GoogleBooksMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleBooksMetadataProvider> _logger;

    public string ProviderName => "GoogleBooks";

    public GoogleBooksMetadataProvider(HttpClient httpClient, ILogger<GoogleBooksMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CoverCandidate>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null)
    {
        var covers = new List<CoverCandidate>();
        string? apiKey = null;
        settings?.TryGetValue("GoogleBooksApiKey", out apiKey);

        // Palabras sueltas (sin comillas): encuentra "El despertar del Leviatán" buscando "leviatan despertar"
        string query = title;
        if (!string.IsNullOrWhiteSpace(author)) query += $" inauthor:{author}";

        string url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=20";
        if (!string.IsNullOrWhiteSpace(apiKey)) url += $"&key={apiKey}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url);
            if (response?.Items != null)
            {
                foreach (var item in response.Items)
                {
                    var info = item.VolumeInfo;
                    var cover = QuimeraReader.Application.Common.CoverPolicy.GoogleCoverUrl(info?.ImageLinks?.Thumbnail);
                    if (cover == null || info == null) continue;
                    covers.Add(new CoverCandidate
                    {
                        ImageUrl = cover,
                        Provider = ProviderName,
                        Title = info.Title,
                        Authors = info.Authors ?? new List<string>(),
                        Synopsis = info.Description,
                        Categories = info.Categories ?? new List<string>(),
                        Rating = info.AverageRating,
                        RatingsCount = info.RatingsCount
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Books: error buscando covers para '{Title}'", title);
        }
        return covers;
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("GoogleBooksApiKey", out apiKey);

        // Intento 1: Por ISBNs si existen (iteramos sobre cada posible ISBN)
        if (isbns != null && isbns.Any())
        {
            foreach (var isbn in isbns)
            {
                if (string.IsNullOrWhiteSpace(isbn)) continue;
                var result = await FetchFromGoogleBooks($"isbn:{isbn}", apiKey);
                if (result != null) return result;
            }
        }

        // Intento 2: Por Título + Autor (Si hay autor disponible)
        if (!string.IsNullOrWhiteSpace(authorHint))
        {
            var resultWithAuthor = await FetchFromGoogleBooks($"intitle:\"{query}\"+inauthor:\"{authorHint}\"", apiKey);
            if (resultWithAuthor != null) return resultWithAuthor;
        }

        // Intento 3: Por Título solamente (Fallback final)
        return await FetchFromGoogleBooks($"intitle:\"{query}\"", apiKey);
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
                CoverImageUri = QuimeraReader.Application.Common.CoverPolicy.GoogleCoverUrl(firstItem.ImageLinks?.Thumbnail),
                Categories = firstItem.Categories ?? new List<string>(),
                Synopsis = firstItem.Description,
                AverageRating = firstItem.AverageRating,
                RatingsCount = firstItem.RatingsCount,
                Source = ProviderName
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Books: error buscando '{Query}'", searchString);
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
        public int? RatingsCount { get; set; }
    }
    internal class ImageLinks { public string? Thumbnail { get; set; } }
}
