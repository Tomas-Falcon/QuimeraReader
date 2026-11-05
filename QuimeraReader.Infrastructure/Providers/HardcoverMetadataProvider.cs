using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Providers;

public class HardcoverMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HardcoverMetadataProvider> _logger;

    public string ProviderName => "Hardcover";

    public HardcoverMetadataProvider(HttpClient httpClient, ILogger<HardcoverMetadataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<CoverCandidate>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("HardcoverApiKey", out apiKey);
        if (string.IsNullOrWhiteSpace(apiKey)) return new List<CoverCandidate>();

        var books = await QueryBooksAsync(title, apiKey, author, limit: 15);
        if (books.Count == 0 && !string.IsNullOrWhiteSpace(author))
        {
            books = await QueryBooksAsync(title, apiKey, null, limit: 15);
        }

        return books
            .Where(b => !string.IsNullOrWhiteSpace(b.Image?.Url))
            .Select(b => new CoverCandidate
            {
                ImageUrl = b.Image!.Url!,
                Provider = ProviderName,
                Title = b.Title,
                Authors = AuthorsOf(b),
                Synopsis = b.Description,
                Categories = TagsOf(b),
                Rating = b.Rating,
                RatingsCount = b.RatingsCount
            })
            .ToList();
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("HardcoverApiKey", out apiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        // Título + autor en la misma búsqueda; si no hay nada, solo el título
        return await FetchFromHardcoverAsync(query, apiKey, authorHint)
               ?? (string.IsNullOrWhiteSpace(authorHint) ? null : await FetchFromHardcoverAsync(query, apiKey, null, authorHint));
    }

    /// <summary>El resultado más relevante cuyo autor coincide (la búsqueda trae también resúmenes y guías de otros autores).</summary>
    private async Task<BookMetadata?> FetchFromHardcoverAsync(string query, string apiKey, string? searchAuthor, string? requiredAuthor = null)
    {
        var mustMatch = requiredAuthor ?? searchAuthor;
        var bookNode = (await QueryBooksAsync(query, apiKey, searchAuthor, limit: 5))
            .FirstOrDefault(b => QuimeraReader.Application.Common.CoverPolicy.AuthorsMatch(mustMatch, AuthorsOf(b)));
        if (bookNode == null) return null;

        return new BookMetadata
        {
            Title = bookNode.Title,
            Authors = AuthorsOf(bookNode),
            CoverImageUri = bookNode.Image?.Url,
            Categories = TagsOf(bookNode),
            Synopsis = bookNode.Description,
            AverageRating = bookNode.Rating,
            RatingsCount = bookNode.RatingsCount,
            Source = ProviderName
        };
    }

    /// <summary>
    /// Búsqueda de texto de Hardcover (aproximada, también por títulos alternativos: "El despertar del Leviatán"
    /// encuentra "Leviathan Wakes"). La consulta con _ilike que se usaba antes la rechaza el servidor desde hace tiempo,
    /// así que este proveedor no devolvía nada.
    /// </summary>
    private async Task<List<HardcoverBook>> QueryBooksAsync(string title, string apiKey, string? authorHint, int limit)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.hardcover.app/v1/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? apiKey[7..] : apiKey);

        var text = string.IsNullOrWhiteSpace(authorHint) ? title : $"{title} {authorHint}";
        request.Content = JsonContent.Create(new
        {
            query = "query SearchBook($q: String!, $perPage: Int!) { search(query: $q, query_type: \"Book\", per_page: $perPage, page: 1) { results } }",
            variables = new { q = text, perPage = limit }
        });

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new List<HardcoverBook>();

            var result = await response.Content.ReadFromJsonAsync<HardcoverSearchResponse>();
            return result?.Data?.Search?.Results?.Hits?.Select(h => h.Document).Where(d => d != null).Cast<HardcoverBook>().ToList()
                   ?? new List<HardcoverBook>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardcover: error buscando '{Query}' (Author: '{Author}')", title, authorHint);
            return new List<HardcoverBook>();
        }
    }

    private static List<string> AuthorsOf(HardcoverBook b) => b.AuthorNames ?? new List<string>();

    private static List<string> TagsOf(HardcoverBook b) => b.Genres ?? new List<string>();

    // JSON mapping classes (results es un objeto Typesense: hits[].document)
    internal class HardcoverSearchResponse { public HardcoverSearchData? Data { get; set; } }
    internal class HardcoverSearchData { public HardcoverSearch? Search { get; set; } }
    internal class HardcoverSearch { public HardcoverResults? Results { get; set; } }
    internal class HardcoverResults { public List<HardcoverHit>? Hits { get; set; } }
    internal class HardcoverHit { public HardcoverBook? Document { get; set; } }
    internal class HardcoverBook
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public double? Rating { get; set; }
        [JsonPropertyName("ratings_count")] public int? RatingsCount { get; set; }
        [JsonPropertyName("author_names")] public List<string>? AuthorNames { get; set; }
        public List<string>? Genres { get; set; }
        public HardcoverImage? Image { get; set; }
    }
    internal class HardcoverImage { public string? Url { get; set; } }
}
