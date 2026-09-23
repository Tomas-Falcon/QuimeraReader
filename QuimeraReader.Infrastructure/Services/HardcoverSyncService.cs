using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Infrastructure;

namespace QuimeraReader.Infrastructure.Services;

public class HardcoverSyncService
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<HardcoverSyncService> _logger;

    public HardcoverSyncService(HttpClient httpClient, AppDbContext dbContext, ILogger<HardcoverSyncService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Sincroniza (Pull) todos los libros leídos en Hardcover con la base de datos local.
    /// </summary>
    public async Task<int> PullReadBooksAsync()
    {
        var apiKeySetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "HardcoverApiKey");
        var syncEnabledSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "HardcoverSyncEnabled");
        
        string? apiKey = apiKeySetting?.Value;
        bool isEnabled = bool.TryParse(syncEnabledSetting?.Value, out var b) && b;

        if (string.IsNullOrWhiteSpace(apiKey) || !isEnabled)
        {
            return 0; // Sync desactivada o sin token
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.hardcover.app/v1/graphql");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Pedimos todos los libros leídos por el usuario actual
        // El status_id para "Read" en Hardcover suele ser el 3, pero para mayor seguridad pediremos todos
        // y filtraremos si es necesario o podemos usar la tabla de UserBooks.
        // Asumiremos la siguiente estructura GraphQL basada en la API de Hardcover
        var graphqlQuery = new
        {
            query = @"
            query GetUserReadBooks {
              me {
                user_books {
                  status_id
                  book {
                    title
                    isbn_13
                    isbn_10
                  }
                }
              }
            }"
        };

        request.Content = JsonContent.Create(graphqlQuery);

        int updatedCount = 0;
        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return 0;

            var result = await response.Content.ReadFromJsonAsync<HardcoverUserBooksResponse>();
            
            var userBooks = result?.Data?.Me?.FirstOrDefault()?.UserBooks;
            if (userBooks == null) return 0;

            // Filtrar solo los leídos (Status ID 3 representa "Leído" habitualmente, pero 
            // ajustaremos si descubrimos que es diferente en la versión final de la API)
            var readBooks = userBooks.Where(ub => ub.StatusId == 3).Select(ub => ub.Book).Where(b => b != null).ToList();

            if (!readBooks.Any()) return 0;

            var localBooks = await _dbContext.Books.ToListAsync();

            foreach (var hcBook in readBooks)
            {
                if (hcBook == null || string.IsNullOrWhiteSpace(hcBook.Title)) continue;

                // Buscar coincidencias locales por ISBN o por Título
                var match = localBooks.FirstOrDefault(b => 
                    (!string.IsNullOrEmpty(b.Isbn) && (b.Isbn == hcBook.Isbn13 || b.Isbn == hcBook.Isbn10)) ||
                    b.Title.Equals(hcBook.Title, StringComparison.OrdinalIgnoreCase)
                );

                if (match != null && !match.IsReadInHardcover)
                {
                    match.IsReadInHardcover = true;
                    match.PercentageCompleted = 100;
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Sincronización con Hardcover completada: {Count} libros marcados como leídos", updatedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sincronizando progreso de Hardcover");
        }

        return updatedCount;
    }

    // Modelos de respuesta JSON
    internal class HardcoverUserBooksResponse { public HardcoverMeData? Data { get; set; } }
    internal class HardcoverMeData { public List<HardcoverMe>? Me { get; set; } }
    internal class HardcoverMe { public List<HardcoverUserBook>? UserBooks { get; set; } }
    internal class HardcoverUserBook 
    { 
        public int? StatusId { get; set; } 
        public HardcoverBookRef? Book { get; set; } 
    }
    internal class HardcoverBookRef 
    { 
        public string? Title { get; set; } 
        public string? Isbn13 { get; set; }
        public string? Isbn10 { get; set; }
    }
}
