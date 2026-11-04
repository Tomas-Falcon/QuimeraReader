using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Copias de seguridad de lo que el usuario ha hecho con sus libros.
/// - JSON (manifest): estado, progreso, sin conexión, notas, saga y universo. Sirve si los archivos siguen en disco.
/// - ZIP: el mismo manifest más EPUB, audios, portada y sincronización de cada libro. Sirve aunque se pierdan los archivos.
/// La restauración identifica cada libro por hash, luego ISBN, luego título + primer autor.
/// </summary>
public class BackupService
{
    public const int ManifestVersion = 1;
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AppDbContext _db;
    private readonly EpubScannerService _scanner;
    private readonly ILogger<BackupService> _logger;

    public BackupService(AppDbContext db, EpubScannerService scanner, ILogger<BackupService> logger)
    {
        _db = db;
        _scanner = scanner;
        _logger = logger;
    }

    public class Manifest
    {
        public int Version { get; set; } = ManifestVersion;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IncludesFiles { get; set; }
        public List<BackupBook> Books { get; set; } = new();
    }

    public class BackupBook
    {
        public int OriginalId { get; set; }
        public string? Hash { get; set; }
        public string? Isbn { get; set; }
        public string Title { get; set; } = "";
        public List<string> Authors { get; set; } = new();
        public string? ReadingStatus { get; set; }
        public DateTime? LastReadAt { get; set; }
        public double? PercentageCompleted { get; set; }
        public string? CurrentEpubCfi { get; set; }
        public double? CurrentAudioPosition { get; set; }
        public int? CurrentAudioTrackNumber { get; set; }
        public bool IsAvailableOffline { get; set; }
        public bool IsReadInHardcover { get; set; }
        public string? SeriesName { get; set; }
        public double? SeriesVolume { get; set; }
        public string? UniverseName { get; set; }
        public string? SyncMapJson { get; set; }
        public List<BackupAnnotation> Annotations { get; set; } = new();
        public List<BackupAudioTrack> AudioTracks { get; set; } = new();
        /// <summary>Rutas dentro del ZIP (null en la copia solo JSON o si el archivo no existía).</summary>
        public string? EpubEntry { get; set; }
        public string? CoverEntry { get; set; }
    }

    public class BackupAnnotation
    {
        public string CfiRange { get; set; } = "";
        public string SelectedText { get; set; } = "";
        public string? Note { get; set; }
        public string? ColorHex { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class BackupAudioTrack
    {
        public int TrackNumber { get; set; }
        public string? FileName { get; set; }
        public string? OriginalPath { get; set; }
        public string? Entry { get; set; }
    }

    public class RestoreReport
    {
        public int BooksInBackup { get; set; }
        public int MatchedByHash { get; set; }
        public int MatchedByIsbn { get; set; }
        public int MatchedByTitle { get; set; }
        public int Imported { get; set; }
        public int AnnotationsAdded { get; set; }
        public int AudioTracksAdded { get; set; }
        public List<string> NotFound { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    private async Task<string?> GetLibraryRootAsync(CancellationToken cancellationToken) =>
        await _db.SystemSettings.AsNoTracking().Where(s => s.Key == "LibraryRootPath").Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Libros con algo del usuario: estado, progreso, sin conexión, notas o audio.</summary>
    public async Task<Manifest> BuildManifestAsync(CancellationToken cancellationToken = default)
    {
        var books = await _db.Books.AsNoTracking()
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Annotations)
            .Include(b => b.AudioTracks)
            .Include(b => b.SyncMap)
            .Include(b => b.Series)
            .Include(b => b.Universe)
            .Where(b => (b.ReadingStatus != null && b.ReadingStatus != "Unread") || b.IsAvailableOffline || b.LastReadAt != null
                        || b.PercentageCompleted > 0 || b.CurrentAudioPosition > 0 || b.CurrentEpubCfi != null
                        || b.Annotations.Any() || b.AudioTracks.Any())
            .AsSplitQuery()
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var manifest = new Manifest();
        foreach (var b in books)
        {
            manifest.Books.Add(new BackupBook
            {
                OriginalId = b.Id,
                Hash = b.ContentHash,
                Isbn = b.Isbn,
                Title = b.Title,
                Authors = b.Authors.Where(a => a.Author != null).Select(a => a.Author.Name).ToList(),
                ReadingStatus = b.ReadingStatus,
                LastReadAt = b.LastReadAt,
                PercentageCompleted = b.PercentageCompleted,
                CurrentEpubCfi = b.CurrentEpubCfi,
                CurrentAudioPosition = b.CurrentAudioPosition,
                CurrentAudioTrackNumber = b.CurrentAudioTrackNumber,
                IsAvailableOffline = b.IsAvailableOffline,
                IsReadInHardcover = b.IsReadInHardcover,
                SeriesName = b.Series?.Name,
                SeriesVolume = b.SeriesVolume,
                UniverseName = b.Universe?.Name,
                SyncMapJson = b.SyncMap?.SyncMapJson,
                Annotations = b.Annotations.OrderBy(a => a.CreatedAt).Select(a => new BackupAnnotation
                {
                    CfiRange = a.CfiRange, SelectedText = a.SelectedText, Note = a.Note, ColorHex = a.ColorHex,
                    CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt
                }).ToList(),
                AudioTracks = b.AudioTracks.OrderBy(t => t.TrackNumber).Select(t => new BackupAudioTrack
                {
                    TrackNumber = t.TrackNumber, FileName = t.FileName, OriginalPath = t.FilePath
                }).ToList()
            });
        }
        return manifest;
    }

    /// <summary>Escribe el ZIP (manifest.json + archivos de cada libro) en <paramref name="output"/>, que debe admitir escritura síncrona.</summary>
    public async Task WriteZipAsync(Stream output, CancellationToken cancellationToken = default)
    {
        var manifest = await BuildManifestAsync(cancellationToken);
        manifest.IncludesFiles = true;
        var libraryRoot = await GetLibraryRootAsync(cancellationToken);

        var paths = await _db.Books.AsNoTracking()
            .Where(b => manifest.Books.Select(m => m.OriginalId).Contains(b.Id))
            .Select(b => new { b.Id, b.EpubFilePath, b.CoverImagePath })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var book in manifest.Books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var epub = LibraryPathResolver.Resolve(paths[book.OriginalId].EpubFilePath, libraryRoot);
            if (epub != null && book.Hash == null)
            {
                book.Hash = await BookFingerprint.ComputeAsync(epub, cancellationToken);
            }
            var folder = $"books/{book.Hash ?? "book-" + book.OriginalId}";

            if (epub != null)
            {
                book.EpubEntry = $"{folder}/book.epub";
                await AddFileAsync(zip, epub, book.EpubEntry, CompressionLevel.NoCompression, cancellationToken);
            }

            var cover = LibraryPathResolver.Resolve(paths[book.OriginalId].CoverImagePath, libraryRoot);
            if (cover != null)
            {
                book.CoverEntry = $"{folder}/cover{Path.GetExtension(cover).ToLowerInvariant()}";
                await AddFileAsync(zip, cover, book.CoverEntry, CompressionLevel.NoCompression, cancellationToken);
            }

            foreach (var track in book.AudioTracks)
            {
                var audio = LibraryPathResolver.Resolve(track.OriginalPath, libraryRoot);
                if (audio == null) continue;
                track.Entry = $"{folder}/audio/{track.TrackNumber:D3}{Path.GetExtension(audio).ToLowerInvariant()}";
                await AddFileAsync(zip, audio, track.Entry, CompressionLevel.NoCompression, cancellationToken);
            }
        }

        var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var stream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        }
    }

    private static async Task AddFileAsync(ZipArchive zip, string path, string entryName, CompressionLevel level, CancellationToken cancellationToken)
    {
        // EPUB, MP3 y JPG ya vienen comprimidos: guardarlos sin comprimir ahorra CPU sin apenas perder espacio
        var entry = zip.CreateEntry(entryName, level);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        await using var target = entry.Open();
        await source.CopyToAsync(target, cancellationToken);
    }

    /// <summary>Restaura un manifest (JSON) o un ZIP. Con ZIP, los libros que no estén en la biblioteca se importan desde él.</summary>
    public async Task<RestoreReport> RestoreAsync(Stream input, bool isZip, CancellationToken cancellationToken = default)
    {
        if (!isZip)
        {
            var manifest = await JsonSerializer.DeserializeAsync<Manifest>(input, JsonOptions, cancellationToken)
                           ?? throw new InvalidDataException("La copia de seguridad está vacía.");
            return await RestoreManifestAsync(manifest, null, cancellationToken);
        }

        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("El ZIP no contiene manifest.json.");
        Manifest? zipManifest;
        await using (var stream = manifestEntry.Open())
        {
            zipManifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken);
        }
        return await RestoreManifestAsync(zipManifest ?? throw new InvalidDataException("manifest.json está vacío."), zip, cancellationToken);
    }

    private async Task<RestoreReport> RestoreManifestAsync(Manifest manifest, ZipArchive? zip, CancellationToken cancellationToken)
    {
        if (manifest.Version > ManifestVersion) throw new InvalidDataException($"Copia de una versión más nueva ({manifest.Version}).");
        var report = new RestoreReport { BooksInBackup = manifest.Books.Count };
        var libraryRoot = await GetLibraryRootAsync(cancellationToken);

        foreach (var item in manifest.Books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = $"{item.Title} ({string.Join(", ", item.Authors)})";
            try
            {
                var book = await FindAsync(item, report, cancellationToken);
                if (book == null && zip != null && item.EpubEntry != null && zip.GetEntry(item.EpubEntry) is { } epubEntry)
                {
                    book = await ImportFromZipAsync(epubEntry, item, cancellationToken);
                    report.Imported++;
                }
                if (book == null)
                {
                    report.NotFound.Add(label);
                    continue;
                }

                await ApplyAsync(book, item, zip, libraryRoot, report, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "No se pudo restaurar {Book}", label);
                report.Errors.Add($"{label}: {ex.Message}");
                _db.ChangeTracker.Clear();
            }
        }
        return report;
    }

    private IQueryable<Book> BooksWithChildren() => _db.Books
        .Include(b => b.Authors).ThenInclude(ba => ba.Author)
        .Include(b => b.Annotations)
        .Include(b => b.AudioTracks)
        .Include(b => b.SyncMap)
        .AsSplitQuery();

    private async Task<Book?> FindAsync(BackupBook item, RestoreReport report, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(item.Hash))
        {
            var byHash = await BooksWithChildren().Where(b => b.ContentHash == item.Hash).OrderBy(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            if (byHash != null) { report.MatchedByHash++; return byHash; }
        }
        if (!string.IsNullOrWhiteSpace(item.Isbn))
        {
            var byIsbn = await BooksWithChildren().Where(b => b.Isbn == item.Isbn).OrderBy(b => b.Id).FirstOrDefaultAsync(cancellationToken);
            if (byIsbn != null) { report.MatchedByIsbn++; return byIsbn; }
        }

        var title = TextNormalizer.Fold(item.Title);
        var author = TextNormalizer.Fold(item.Authors.FirstOrDefault());
        var candidates = await BooksWithChildren().Where(b => EF.Functions.Collate(b.Title, "NOCASE") == item.Title).ToListAsync(cancellationToken);
        var byTitle = candidates.FirstOrDefault(b => TextNormalizer.Fold(b.Title) == title
                                                     && (author.Length == 0 || b.Authors.Any(a => a.Author != null && TextNormalizer.Fold(a.Author.Name) == author)));
        if (byTitle != null) report.MatchedByTitle++;
        return byTitle;
    }

    private async Task<Book> ImportFromZipAsync(ZipArchiveEntry entry, BackupBook item, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"quimera-restore-{Guid.NewGuid():N}.epub");
        await using (var source = entry.Open())
        await using (var target = new FileStream(tempPath, FileMode.CreateNew))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        // Igual que una subida desde la app: el escáner lo organiza en la biblioteca y lee sus metadatos
        var book = await _scanner.ScanEpubAsync(tempPath, "GoogleBooks", SafeName(item.Title) + ".epub", forceMove: true);
        if (book.Id == 0) _db.Books.Add(book);
        await _db.SaveChangesAsync(cancellationToken);
        if (File.Exists(tempPath)) File.Delete(tempPath);
        return await BooksWithChildren().FirstAsync(b => b.Id == book.Id, cancellationToken);
    }

    private static string SafeName(string title) => LibraryPathUtils.SafeFileName(title, "libro");

    private async Task ApplyAsync(Book book, BackupBook item, ZipArchive? zip, string? libraryRoot, RestoreReport report, CancellationToken cancellationToken)
    {
        // El progreso más reciente gana (misma regla que la sincronización)
        if (item.LastReadAt != null && (book.LastReadAt == null || item.LastReadAt > book.LastReadAt))
        {
            book.LastReadAt = item.LastReadAt;
            book.CurrentEpubCfi = item.CurrentEpubCfi ?? book.CurrentEpubCfi;
            book.PercentageCompleted = item.PercentageCompleted ?? book.PercentageCompleted;
            book.CurrentAudioPosition = item.CurrentAudioPosition ?? book.CurrentAudioPosition;
            book.CurrentAudioTrackNumber = item.CurrentAudioTrackNumber ?? book.CurrentAudioTrackNumber;
        }
        else if (book.LastReadAt == null && book.CurrentEpubCfi == null)
        {
            book.CurrentEpubCfi = item.CurrentEpubCfi;
            book.PercentageCompleted ??= item.PercentageCompleted;
            book.CurrentAudioPosition ??= item.CurrentAudioPosition;
            book.CurrentAudioTrackNumber ??= item.CurrentAudioTrackNumber;
        }
        if ((string.IsNullOrEmpty(book.ReadingStatus) || book.ReadingStatus == "Unread") && !string.IsNullOrEmpty(item.ReadingStatus))
            book.ReadingStatus = item.ReadingStatus;
        book.IsAvailableOffline |= item.IsAvailableOffline;
        book.IsReadInHardcover |= item.IsReadInHardcover;
        book.ContentHash ??= item.Hash;

        if (book.SeriesId == null && !string.IsNullOrWhiteSpace(item.SeriesName))
        {
            book.Series = await SeriesAssigner.FindOrCreateAsync(_db, item.SeriesName, cancellationToken);
            book.SeriesVolume ??= item.SeriesVolume;
        }
        if (book.UniverseId == null && !string.IsNullOrWhiteSpace(item.UniverseName))
        {
            book.Universe = await _db.Universes.FirstOrDefaultAsync(u => EF.Functions.Collate(u.Name, "NOCASE") == item.UniverseName, cancellationToken)
                            ?? _db.Universes.Add(new Universe { Name = item.UniverseName }).Entity;
        }

        foreach (var note in item.Annotations)
        {
            if (book.Annotations.Any(a => a.CfiRange == note.CfiRange && a.SelectedText == note.SelectedText)) continue;
            book.Annotations.Add(new BookAnnotation
            {
                CfiRange = note.CfiRange, SelectedText = note.SelectedText, Note = note.Note, ColorHex = note.ColorHex,
                CreatedAt = note.CreatedAt, UpdatedAt = note.UpdatedAt
            });
            report.AnnotationsAdded++;
        }

        if (book.SyncMap == null && !string.IsNullOrEmpty(item.SyncMapJson))
        {
            book.SyncMap = new SyncMap { SyncMapJson = item.SyncMapJson };
        }

        await RestoreAudioAsync(book, item, zip, libraryRoot, report, cancellationToken);
        await RestoreCoverAsync(book, item, zip, libraryRoot, cancellationToken);
    }

    private async Task RestoreAudioAsync(Book book, BackupBook item, ZipArchive? zip, string? libraryRoot, RestoreReport report, CancellationToken cancellationToken)
    {
        if (item.AudioTracks.Count == 0) return;
        var existing = book.AudioTracks.Select(t => LibraryPathResolver.Resolve(t.FilePath, libraryRoot)).Where(p => p != null).Select(p => Path.GetFileName(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Si el libro ya tiene todas sus pistas con archivo, no se duplican
        if (book.AudioTracks.Count >= item.AudioTracks.Count && existing.Count == book.AudioTracks.Count) return;

        var bookDir = Path.GetDirectoryName(LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot) ?? book.EpubFilePath) ?? libraryRoot ?? Path.GetTempPath();
        int next = book.AudioTracks.Count == 0 ? 1 : book.AudioTracks.Max(t => t.TrackNumber) + 1;

        foreach (var track in item.AudioTracks.OrderBy(t => t.TrackNumber))
        {
            string? path = null;
            if (zip != null && track.Entry != null && zip.GetEntry(track.Entry) is { } entry)
            {
                Directory.CreateDirectory(bookDir);
                path = LibraryPathUtils.GetUniqueFilePath(bookDir, $"{SafeName(book.Title)} track {track.TrackNumber}{Path.GetExtension(track.Entry)}");
                await using var source = entry.Open();
                await using var target = new FileStream(path, FileMode.CreateNew);
                await source.CopyToAsync(target, cancellationToken);
            }
            else
            {
                // Solo JSON: reutilizar el audio original si sigue en disco
                path = LibraryPathResolver.Resolve(track.OriginalPath, libraryRoot);
            }
            if (path == null || book.AudioTracks.Any(t => t.FilePath == path)) continue;

            book.AudioTracks.Add(new BookAudioTrack { FilePath = path, FileName = track.FileName ?? Path.GetFileName(path), TrackNumber = next++ });
            report.AudioTracksAdded++;
        }
    }

    private async Task RestoreCoverAsync(Book book, BackupBook item, ZipArchive? zip, string? libraryRoot, CancellationToken cancellationToken)
    {
        if (zip == null || item.CoverEntry == null || LibraryPathResolver.Resolve(book.CoverImagePath, libraryRoot) != null) return;
        if (zip.GetEntry(item.CoverEntry) is not { } entry) return;
        var dir = Path.GetDirectoryName(LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot) ?? "") ;
        if (string.IsNullOrEmpty(dir)) return;

        var path = Path.Combine(dir, "cover" + Path.GetExtension(item.CoverEntry));
        await using (var source = entry.Open())
        await using (var target = new FileStream(path, FileMode.Create))
        {
            await source.CopyToAsync(target, cancellationToken);
        }
        book.CoverImagePath = path;
    }

    /// <summary>
    /// Copia consistente de la base de datos con el servidor en marcha (VACUUM INTO) en la carpeta backups junto a ella.
    /// Conserva las <paramref name="keep"/> más recientes.
    /// </summary>
    public static async Task<string> SnapshotDatabaseAsync(AppDbContext db, string reason, int keep = 5, CancellationToken cancellationToken = default)
    {
        var dbPath = db.Database.GetDbConnection().DataSource;
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "backups");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"quimerareader-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{LibraryPathUtils.SafeFileName(reason, "manual")}.db");

        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{target.Replace("'", "''")}'", cancellationToken);

        foreach (var old in new DirectoryInfo(dir).GetFiles("quimerareader-*.db").OrderByDescending(f => f.Name).Skip(keep))
        {
            try { old.Delete(); } catch { }
        }
        return target;
    }
}
