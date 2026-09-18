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

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null)
    {
        string? apiKey = null;
        settings?.TryGetValue("HardcoverApiKey", out apiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Hardcover requires an API Key
            return null;
        }

        // Prepare GraphQL Request
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.hardcover.app/v1/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Intento 1: Por título (Hardcover GraphQL)
        // Hardcover API documentation typically requires GraphQL queries. 
        // A generic title query sorting by users_count to get the most relevant book:
        var graphqlQuery = new
        {
            query = @"
            query SearchBook($title: String!) {
              books(
                where: { title: { _ilike: $title } }
                limit: 1
                order_by: { users_count: desc }
              ) {
                title
                description
                rating
                contributions {
                  author {
                    name
                  }
                }
                image {
                  url
                }
                tags {
                  tag {
                    name
                  }
                }
              }
            }",
            variables = new { title = $"%{query}%" }
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
            _logger.LogWarning(ex, "Hardcover: error buscando '{Query}'", query);
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
