using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Domain.Interfaces;
using VersOne.Epub;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Repara portadas que no existen en disco o que apuntan a una URL externa: primero la saca del propio EPUB y,
/// si no trae, la busca en OpenLibrary (por ISBN) y en los proveedores de metadatos (título + autor).
/// Siempre se guarda una copia local en la biblioteca, para no depender de que el EPUB original
/// (p. ej. en la carpeta del torrent) o el servicio externo sigan ahí.
/// </summary>
public class CoverRepairService
{
    private const int MinImageBytes = 1024;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<CoverRepairService> _logger;

    public CoverRepairService(AppDbContext db, IHttpClientFactory httpClientFactory, IEnumerable<IMetadataProvider> providers, ILogger<CoverRepairService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _logger = logger;
    }

    public class Report
    {
        public bool Online { get; set; }
        public int Checked { get; set; }
        public int AlreadyOk { get; set; }
        public int NeedRepair { get; set; }
        public int FromEpub { get; set; }
        public int FromOnline { get; set; }
        public int NotFound { get; set; }
        /// <summary>Libros sin EPUB en disco: no se tocan (los resuelve la limpieza de duplicados).</summary>
        public int SkippedMissingEpub { get; set; }
        public int Errors { get; set; }
        public bool Done { get; set; }
    }

    public enum Source { None, Epub, Online }

    private async Task<Dictionary<string, string>> SettingsAsync(CancellationToken cancellationToken) =>
        await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

    /// <summary>¿La portada del libro existe como archivo local?</summary>
    public static bool HasLocalCover(Book book, string? libraryRoot) =>
        !string.IsNullOrEmpty(book.CoverImagePath)
        && !book.CoverImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        && LibraryPathResolver.Resolve(book.CoverImagePath, libraryRoot) != null;

    /// <summary>
    /// Repara la portada de un libro. Con <paramref name="online"/>=false solo la saca del EPUB (rápido, para servirla al vuelo).
    /// No guarda en la base de datos: lo hace quien llama.
    /// </summary>
    public async Task<Source> RepairAsync(Book book, string? libraryRoot, bool online, Dictionary<string, string>? settings = null, CancellationToken cancellationToken = default)
    {
        var epubPath = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);

        byte[]? bytes = null;
        var source = Source.None;
        if (epubPath != null)
        {
            bytes = await ReadEpubCoverAsync(epubPath);
            if (bytes != null) source = Source.Epub;
        }

        if (bytes == null && online)
        {
            settings ??= await SettingsAsync(cancellationToken);
            bytes = await DownloadOnlineCoverAsync(book, settings, cancellationToken);
            if (bytes != null) source = Source.Online;
        }

        if (bytes == null) return Source.None;

        var path = CoverPathFor(book, epubPath, libraryRoot, ImageExtension(bytes));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        book.CoverImagePath = path;
        return source;
    }

    /// <summary>
    /// Junto al EPUB si está dentro de la biblioteca; si no (EPUB en la carpeta del torrent), en
    /// biblioteca/_covers, para no escribir en la carpeta de descargas.
    /// </summary>
    public static string CoverPathFor(Book book, string? epubPath, string? libraryRoot, string extension)
    {
        var root = string.IsNullOrWhiteSpace(libraryRoot) ? QuimeraReader.Application.Common.LibraryPathUtils.DefaultLibraryRoot : libraryRoot;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var epubDir = epubPath != null ? Path.GetDirectoryName(Path.GetFullPath(epubPath)) : null;

        if (epubDir != null && (epubDir + Path.DirectorySeparatorChar).StartsWith(rootFull, StringComparison.Ordinal))
        {
            return Path.Combine(epubDir, "cover" + extension);
        }
        return Path.Combine(root, "_covers", (book.Id / 1000).ToString("D3"), book.Id + extension);
    }

    public async Task<byte[]?> ReadEpubCoverAsync(string epubPath)
    {
        try
        {
            using var bookRef = await EpubReader.OpenBookAsync(epubPath);
            var cover = await bookRef.ReadCoverAsync();
            return IsImage(cover) ? cover : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer la portada del EPUB {Path}", epubPath);
            return null;
        }
    }

    private async Task<byte[]?> DownloadOnlineCoverAsync(Book book, Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        if (!string.IsNullOrEmpty(book.CoverImagePath) && book.CoverImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            urls.Add(book.CoverImagePath.Replace("http://", "https://"));
        }
        if (!string.IsNullOrWhiteSpace(book.Isbn))
        {
            var isbn = new string(book.Isbn.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
            if (isbn.Length is 10 or 13) urls.Add($"https://covers.openlibrary.org/b/isbn/{isbn}-L.jpg?default=false");
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        var tried = await TryDownloadAnyAsync(client, urls, cancellationToken);
        if (tried != null) return tried;

        var author = book.Authors.FirstOrDefault()?.Author?.Name;
        foreach (var name in new[] { "GoogleBooks", "OpenLibrary" })
        {
            var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderName, name, StringComparison.OrdinalIgnoreCase));
            if (provider == null) continue;
            try
            {
                var found = await provider.SearchCoversAsync(book.Title, author, settings);
                var bytes = await TryDownloadAnyAsync(client, found.Take(2).Select(c => c.ImageUrl), cancellationToken);
                if (bytes != null) return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "{Provider}: no se pudo buscar portada para {Title}", name, book.Title);
            }
        }
        return null;
    }

    private static async Task<byte[]?> TryDownloadAnyAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (IsImage(bytes)) return bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return null;
    }

    /// <summary>JPEG, PNG, GIF o WebP de un tamaño razonable (los "sin portada" de algunos servicios pesan unos bytes).</summary>
    public static bool IsImage(byte[]? bytes) => bytes != null && bytes.Length >= MinImageBytes && ImageExtension(bytes) != "";

    public static string ImageExtension(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return ".gif";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return "";
    }

    /// <summary>Recorre la biblioteca reparando las portadas que falten. Por lotes; online con pausa para no saturar los servicios externos.</summary>
    public async Task<Report> RepairAllAsync(bool online, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var report = new Report { Online = online };
        var settings = await SettingsAsync(cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);

        int lastId = 0;
        while (true)
        {
            var batch = await _db.Books
                .Include(b => b.Authors).ThenInclude(ba => ba.Author)
                .Where(b => b.Id > lastId)
                .OrderBy(b => b.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            foreach (var book in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.Checked++;
                if (HasLocalCover(book, libraryRoot)) { report.AlreadyOk++; continue; }
                if (LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot) == null) { report.SkippedMissingEpub++; continue; }

                report.NeedRepair++;
                try
                {
                    var source = await RepairAsync(book, libraryRoot, online, settings, cancellationToken);
                    switch (source)
                    {
                        case Source.Epub: report.FromEpub++; break;
                        case Source.Online:
                            report.FromOnline++;
                            await Task.Delay(500, cancellationToken);
                            break;
                        default:
                            report.NotFound++;
                            if (online) await Task.Delay(500, cancellationToken);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    report.Errors++;
                    _logger.LogDebug(ex, "No se pudo reparar la portada del libro {Id}", book.Id);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            onProgress?.Invoke(report.Checked);
        }

        report.Done = true;
        return report;
    }
}
