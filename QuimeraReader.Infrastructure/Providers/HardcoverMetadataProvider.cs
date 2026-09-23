using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
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

        public async Task<List<string>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null)
    {
        // For simplicity, Hardcover search might be complex via GraphQL for multiple generic covers, 
        // we'll just return an empty list or implement it if easy.
        // Returning empty list for now since Google Books and OpenLibrary will provide enough.
        return await Task.FromResult(new List<string>());
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("HardcoverApiKey", out apiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        // Si tenemos un autor, intentamos primero filtrando por autor en GraphQL
        if (!string.IsNullOrWhiteSpace(authorHint))
        {
            var resultWithAuthor = await FetchFromHardcoverAsync(query, apiKey, authorHint);
            if (resultWithAuthor != null) return resultWithAuthor;
        }

        // Fallback: solo título
        return await FetchFromHardcoverAsync(query, apiKey, null);
    }

    private async Task<BookMetadata?> FetchFromHardcoverAsync(string query, string apiKey, string? authorHint)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.hardcover.app/v1/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Si hay autor, agregamos una condición a Contributions
        string authorCondition = string.IsNullOrWhiteSpace(authorHint) 
            ? "" 
            : @", contributions: { author: { name: { _ilike: $author } } }";

        var graphqlQuery = new
        {
            query = $@"
            query SearchBook($title: String!, $author: String!) {{
              books(
                where: {{ title: {{ _ilike: $title }} {authorCondition} }}
                limit: 1
                order_by: {{ users_count: desc }}
              ) {{
                title
                description
                rating
                contributions {{
                  author {{
                    name
                  }}
                }}
                image {{
                  url
                }}
                tags {{
                  tag {{
                    name
                  }}
                }}
              }}
            }}",
            variables = new { title = $"%{query}%", author = $"%{authorHint}%" }
        };

        request.Content = JsonContent.Create(graphqlQuery);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<HardcoverGraphqlResponse>();
            var bookNode = result?.Data?.Books?.FirstOrDefault();

            if (bookNode == null) return null;

            return new BookMetadata
            {
                Title = bookNode.Title,
                Authors = bookNode.Contributions?.Select(c => c.Author?.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList() ?? new List<string>(),
                CoverImageUri = bookNode.Image?.Url,
                Categories = bookNode.Tags?.Select(t => t.Tag?.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList() ?? new List<string>(),
                Synopsis = bookNode.Description,
                AverageRating = bookNode.Rating
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardcover: error buscando '{Query}' (Author: '{Author}')", query, authorHint);
            return null;
        }
    }

    // JSON mapping classes
    internal class HardcoverGraphqlResponse { public HardcoverData? Data { get; set; } }
    internal class HardcoverData { public List<HardcoverBook>? Books { get; set; } }
    internal class HardcoverBook 
    { 
        public string? Title { get; set; } 
        public string? Description { get; set; }
        public double? Rating { get; set; }
        public HardcoverImage? Image { get; set; }
        public List<HardcoverContribution>? Contributions { get; set; }
        public List<HardcoverTagEdge>? Tags { get; set; }
    }
    internal class HardcoverImage { public string? Url { get; set; } }
    internal class HardcoverContribution { public HardcoverAuthor? Author { get; set; } }
    internal class HardcoverAuthor { public string? Name { get; set; } }
    internal class HardcoverTagEdge { public HardcoverTag? Tag { get; set; } }
    internal class HardcoverTag { public string? Name { get; set; } }
}
