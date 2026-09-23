using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using QuimeraReader.Domain.Interfaces;
using System.Linq;

namespace QuimeraReader.Infrastructure.Providers;

public class StoryGraphScraperProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StoryGraphScraperProvider> _logger;

    public string ProviderName => "StoryGraph";

    public StoryGraphScraperProvider(HttpClient httpClient, ILogger<StoryGraphScraperProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BookMetadata?> GetMetadataAsync(string query, IEnumerable<string>? isbns = null, Dictionary<string, string>? settings = null, string? authorHint = null)
    {
        return null;
    }

    public async Task<List<string>> SearchCoversAsync(string title, string? author, Dictionary<string, string>? settings = null)
    {
        var covers = new List<string>();
        try
        {
            string query = title;
            if (!string.IsNullOrWhiteSpace(author)) query += " " + author;
            
            string url = $"https://app.thestorygraph.com/browse?search_term={Uri.EscapeDataString(query)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Storygraph uses img tags with the book cover. Usually inside an anchor.
                // We'll search for images that contain "amazon" or "cdn" and don't seem like profile avatars.
                var imgNodes = doc.DocumentNode.SelectNodes("//img");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrWhiteSpace(src) && src.Contains("images.amazon.com"))
                        {
                            // Remove Amazon scaling suffixes
                            src = System.Text.RegularExpressions.Regex.Replace(src, @"\._[A-Z0-9]+_\.", ".");
                            covers.Add(src);
                        }
                        else if (!string.IsNullOrWhiteSpace(src) && src.Contains("storage.googleapis.com/thestorygraph"))
                        {
                            covers.Add(src);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StoryGraphScraper: error buscando covers para '{Title}'", title);
        }
        return covers.Distinct().ToList();
    }
}