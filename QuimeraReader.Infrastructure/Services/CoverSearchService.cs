using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Buscador de portadas del editor (al estilo Jellyfin): busca en todos los proveedores con el título y el autor que
/// escriba el usuario, mide cada imagen y permite aplicar una completando de paso los datos que le falten al libro.
/// </summary>
public class CoverSearchService
{
    public const string EpubProvider = "EPUB";
    private const int MaxImageBytes = 15 * 1024 * 1024;
    // Lo que se lee de cada imagen para medirla: la cabecera está al principio
    private const int HeaderBytes = 64 * 1024;
    // Las miniaturas no sirven como portada
    private const int MinWidth = 150;

    private readonly AppDbContext _db;
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly CoverRepairService _coverRepair;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoverSearchService> _logger;

    public CoverSearchService(AppDbContext db, IEnumerable<IMetadataProvider> providers, CoverRepairService coverRepair,
        IHttpClientFactory httpClientFactory, ILogger<CoverSearchService> logger)
    {
        _db = db;
        _providers = providers;
        _coverRepair = coverRepair;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public class CoverOption
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? Title { get; set; }
        public List<string> Authors { get; set; } = new();
        public string? Synopsis { get; set; }
        public List<string> Categories { get; set; } = new();
        public double? Rating { get; set; }
        public int? RatingsCount { get; set; }
        /// <summary>El autor del resultado coincide con el del libro (los que no, suelen ser otro libro).</summary>
        public bool AuthorMatches { get; set; }
    }

    public class ApplyRequest
    {
        public string? ImageUrl { get; set; }
        public string? Provider { get; set; }
        /// <summary>Rellenar sinopsis, puntuación y categorías si el libro no las tiene.</summary>
        public bool FillMissingMetadata { get; set; }
        public string? Synopsis { get; set; }
        public List<string> Categories { get; set; } = new();
        public double? Rating { get; set; }
        public int? RatingsCount { get; set; }
    }

    public async Task<List<CoverOption>> SearchAsync(Book book, string? title, string? author, string? provider, CancellationToken cancellationToken = default)
    {
        var bookAuthor = book.Authors.FirstOrDefault()?.Author?.Name;
        // Sin filtros, los del libro; si el usuario escribió un título, el autor vacío significa "cualquier autor"
        bool customSearch = !string.IsNullOrWhiteSpace(title);
        author = customSearch ? author?.Trim() : bookAuthor;
        title = customSearch ? title!.Trim() : book.Title;
        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var options = new List<CoverOption>();

        if (string.IsNullOrEmpty(provider) || provider == EpubProvider)
        {
            var epubCover = await ReadEpubCoverAsync(book, settings);
            if (epubCover != null)
            {
                var size = CoverPolicy.GetImageSize(epubCover);
                options.Add(new CoverOption
                {
                    ImageUrl = $"api/media/books/{book.Id}/epub-cover",
                    Provider = EpubProvider,
                    Width = size?.Width,
                    Height = size?.Height,
                    Title = book.Title,
                    Authors = bookAuthor != null ? new List<string> { bookAuthor } : new List<string>(),
                    AuthorMatches = true
                });
            }
        }

        var providers = _providers.Where(p => string.IsNullOrEmpty(provider) || p.ProviderName == provider).ToList();
        var searches = providers.Select(async p =>
        {
            try
            {
                var search = p.SearchCoversAsync(title, string.IsNullOrWhiteSpace(author) ? null : author, settings);
                // Un proveedor lento no puede bloquear la búsqueda entera
                var finished = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(12), cancellationToken));
                return finished == search ? await search : new List<CoverCandidate>();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "{Provider}: error buscando portadas para '{Title}'", p.ProviderName, title);
                return new List<CoverCandidate>();
            }
        });
        var candidates = (await Task.WhenAll(searches)).SelectMany(c => c)
            .Where(c => Uri.TryCreate(c.ImageUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .GroupBy(c => c.ImageUrl).Select(g => g.First())
            .Take(60)
            .ToList();

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using var throttle = new SemaphoreSlim(8);
        var measured = await Task.WhenAll(candidates.Select(async c =>
        {
            await throttle.WaitAsync(cancellationToken);
            try { return (Candidate: c, Size: await MeasureAsync(client, c.ImageUrl, cancellationToken)); }
            finally { throttle.Release(); }
        }));

        foreach (var (c, size) in measured)
        {
            // Sin respuesta o una miniatura/imagen vacía: fuera
            if (size == null || size.Value.Width < MinWidth) continue;
            options.Add(new CoverOption
            {
                ImageUrl = c.ImageUrl.Replace("http://", "https://"),
                Provider = c.Provider,
                Width = size.Value.Width,
                Height = size.Value.Height,
                Title = c.Title,
                Authors = c.Authors,
                Synopsis = c.Synopsis,
                Categories = c.Categories,
                Rating = c.Rating,
                RatingsCount = c.RatingsCount,
                AuthorMatches = bookAuthor == null || c.Authors.Count == 0 || CoverPolicy.AuthorsMatch(bookAuthor, c.Authors)
            });
        }

        // La del EPUB primero; después las del mismo autor, las más grandes antes
        return options
            .OrderByDescending(o => o.Provider == EpubProvider)
            .ThenByDescending(o => o.AuthorMatches)
            .ThenByDescending(o => o.Height ?? 0)
            .ToList();
    }

    public async Task<byte[]?> ReadEpubCoverAsync(Book book, Dictionary<string, string>? settings = null)
    {
        settings ??= await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);
        var epubPath = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);
        return epubPath == null ? null : await _coverRepair.ReadEpubCoverAsync(epubPath);
    }

    /// <summary>Guarda la portada elegida junto al libro (nunca en la carpeta del torrent) y completa los datos que falten.</summary>
    public async Task<bool> ApplyAsync(Book book, ApplyRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);

        byte[]? bytes;
        if (request.Provider == EpubProvider)
        {
            bytes = await ReadEpubCoverAsync(book, settings);
        }
        else
        {
            if (!Uri.TryCreate(request.ImageUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;
            bytes = await DownloadAsync(uri, cancellationToken);
        }
        if (!CoverRepairService.IsImage(bytes)) return false;

        var epubPath = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);
        var path = CoverRepairService.CoverPathFor(book, epubPath, libraryRoot, CoverRepairService.ImageExtension(bytes!));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes!, cancellationToken);
        book.CoverImagePath = path;

        if (request.FillMissingMetadata)
        {
            if (string.IsNullOrWhiteSpace(book.Description) && !string.IsNullOrWhiteSpace(request.Synopsis))
                book.Description = request.Synopsis;
            // La puntuación con más votos de toda la búsqueda (no la de la portada elegida); reemplaza a una con menos votos
            if (request.Rating is double rating && request.RatingsCount is int votes)
                RatingLookup.Apply(book, new RatingPolicy.Rating(rating, votes));
            await AddCategoriesIfMissingAsync(book, request.Categories, cancellationToken);
        }
        return true;
    }

    private async Task AddCategoriesIfMissingAsync(Book book, List<string> categories, CancellationToken cancellationToken)
    {
        await _db.Entry(book).Collection(b => b.Categories).Query().Include(bc => bc.Category).LoadAsync(cancellationToken);
        bool hasRealCategories = book.Categories.Any(bc => bc.Category != null && bc.Category.Name != "Falta Metadatos");
        if (hasRealCategories || categories.Count == 0) return;

        foreach (var name in categories.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().Take(5))
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Name == name, cancellationToken)
                           ?? _db.Categories.Add(new Category { Name = name, IsUserGenerated = false }).Entity;
            book.Categories.Add(new BookCategory { Book = book, Category = category });
        }
    }

    private async Task<(int Width, int Height)?> MeasureAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url.Replace("http://", "https://"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[HeaderBytes];
            int read = 0, n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0) read += n;
            return CoverPolicy.GetImageSize(buffer[..read]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length > MaxImageBytes ? null : bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo descargar la portada {Url}", uri);
            return null;
        }
    }
}
