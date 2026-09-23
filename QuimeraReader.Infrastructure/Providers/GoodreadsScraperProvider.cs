using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using QuimeraReader.Domain.Interfaces;
using System.Linq;

namespace QuimeraReader.Infrastructure.Providers;

public class GoodreadsScraperProvider : IMetadataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoodreadsScraperProvider> _logger;

    public string ProviderName => "Goodreads";

    public GoodreadsScraperProvider(HttpClient httpClient, ILogger<GoodreadsScraperProvider> logger)
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
            
            string url = $"https://www.goodreads.com/search?q={Uri.EscapeDataString(query)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Goodreads search results usually have cover images with class "bookCover"
                var imgNodes = doc.DocumentNode.SelectNodes("//img[contains(@class, 'bookCover')]");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            // Remove thumbnail suffixes (e.g. "._SY75_" or "._SX50_") to get a larger image
                            // For example: https://images.gr-assets.com/books/1361039443s/41865.jpg
                            // Actually it's often easiest to replace 's' or 'i' with 'l' for large, but it depends.
                            // Let's just grab the src.
                            src = System.Text.RegularExpressions.Regex.Replace(src, @"(books/\d+)[siml]/", "$1l/");
                            // Safely upscale Goodreads images to large without corrupting the URL structure
                            
                            // Remove Amazon scaling suffixes
                            src = System.Text.RegularExpressions.Regex.Replace(src, @"\._[A-Z0-9]+_\.", ".");
                            covers.Add(src);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GoodreadsScraper: error buscando covers para '{Title}'", title);
        }
        return covers.Distinct().ToList();
    }
}