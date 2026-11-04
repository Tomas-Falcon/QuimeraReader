using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// "Borrón y cuenta nueva": vacía los libros de la base de datos para volver a escanear desde cero, sin perder nada:
/// - antes: copia de la base de datos, copia completa (ZIP) de lo del usuario y caché de metadatos por hash;
/// - se conservan los ajustes (rutas, modo, claves de API), los audios huérfanos y la caché;
/// - después: se restaura el ZIP (los libros en lectura y sin conexión primero) y se reimportan los EPUB que solo
///   existen en la biblioteca (subidos o del antiguo modo mover), porque el escaneo de la carpeta de entrada no los vería.
/// El resto lo reimporta el escáner normal; la caché evita volver a consultar los servicios externos.
/// Los archivos en disco no se borran.
/// </summary>
public class LibraryResetService
{
    public const string HashBackfillVersionKey = "ContentHashBackfillVersion";

    private readonly AppDbContext _db;
    private readonly BackupService _backup;
    private readonly EpubScannerService _scanner;
    private readonly LibraryScanState _scanState;
    private readonly ILogger<LibraryResetService> _logger;

    public LibraryResetService(AppDbContext db, BackupService backup, EpubScannerService scanner, LibraryScanState scanState, ILogger<LibraryResetService> logger)
    {
        _db = db;
        _backup = backup;
        _scanner = scanner;
        _scanState = scanState;
        _logger = logger;
    }

    public class Plan
    {
        public int TotalBooks { get; set; }
        public int BooksWithFile { get; set; }
        public int BooksWithoutFile { get; set; }
        public int BooksWithHash { get; set; }
        public bool HashBackfillDone { get; set; }
        public int BooksWithUserData { get; set; }
        public int Universes { get; set; }
        /// <summary>EPUB que solo están en la biblioteca (archivo real, no enlace a la carpeta de entrada): se reimportan desde ahí.</summary>
        public int LibraryOnlyEpubs { get; set; }
        public List<string> LibraryOnlySamples { get; set; } = new();
        public int UnmatchedAudiosKept { get; set; }
        public int SettingsKept { get; set; }
        public string? IngestionMode { get; set; }
        public string? IncomingScanFolder { get; set; }
        public List<string> Blockers { get; set; } = new();
        public bool CanRun => Blockers.Count == 0;
    }

    public class Result
    {
        public Plan Plan { get; set; } = new();
        public string? DatabaseSnapshot { get; set; }
        public string? ZipBackup { get; set; }
        public int MetadataCached { get; set; }
        public int BooksDeleted { get; set; }
        public BackupService.RestoreReport? Restore { get; set; }
        public int LibraryOnlyImported { get; set; }
        public int LibraryOnlyErrors { get; set; }
        public string? PendingRestore { get; set; }
    }

    private static bool IsRealFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget == null;
    }

    private static bool IsUnder(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        var r = Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.Ordinal);
    }

    public async Task<Plan> PlanAsync(CancellationToken cancellationToken = default, Action<string>? phase = null, Action<int>? progress = null)
    {
        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);
        settings.TryGetValue("IncomingScanFolder", out var incoming);
        settings.TryGetValue("IngestionMode", out var mode);

        phase?.Invoke("Leyendo tus libros con datos");
        var plan = new Plan
        {
            SettingsKept = settings.Count,
            IngestionMode = mode,
            IncomingScanFolder = incoming,
            HashBackfillDone = settings.TryGetValue(HashBackfillVersionKey, out var v) && v == "1",
            Universes = await _db.Universes.CountAsync(cancellationToken),
            UnmatchedAudiosKept = await _db.UnmatchedAudioTracks.CountAsync(cancellationToken),
            BooksWithUserData = (await _backup.BuildManifestAsync(cancellationToken)).Books.Count
        };

        phase?.Invoke("Comprobando archivos");
        var books = await _db.Books.AsNoTracking().Select(b => new { b.Id, b.EpubFilePath, b.ContentHash }).ToListAsync(cancellationToken);
        int checkedBooks = 0;
        plan.TotalBooks = books.Count;
        plan.BooksWithHash = books.Count(b => b.ContentHash != null);
        foreach (var book in books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++checkedBooks % 1000 == 0) progress?.Invoke(checkedBooks);
            var path = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);
            if (path == null) { plan.BooksWithoutFile++; continue; }
            plan.BooksWithFile++;
            if (IsUnder(path, libraryRoot) && IsRealFile(path) && !IsUnder(path, incoming))
            {
                plan.LibraryOnlyEpubs++;
                if (plan.LibraryOnlySamples.Count < 10) plan.LibraryOnlySamples.Add(path);
            }
        }

        if (!plan.HashBackfillDone) plan.Blockers.Add("Todavía se están calculando los hashes de los EPUB: sin ellos la caché de metadatos no sirve y habría que volver a consultar todo a los servicios externos.");
        if (string.IsNullOrWhiteSpace(incoming) || !Directory.Exists(incoming)) plan.Blockers.Add($"La carpeta de entrada \"{incoming}\" no existe: el escáner no podría volver a importar nada.");
        if (mode != "LeaveInPlace") plan.Blockers.Add("El modo de importación no es torrent (dejar en su sitio): un escaneo desde cero movería los archivos de la carpeta de entrada.");
        return plan;
    }

    public async Task<Result> RunAsync(Action<string> phase, Action<int> progress, CancellationToken cancellationToken = default)
    {
        var result = new Result { Plan = await PlanAsync(cancellationToken) };
        if (!result.Plan.CanRun) throw new InvalidOperationException(string.Join(" ", result.Plan.Blockers));

        _scanState.Paused = true;
        try
        {
            // Esperar a que el escáner suelte el lote en curso
            for (int i = 0; i < 120 && _scanState.IsScanning; i++) await Task.Delay(1000, cancellationToken);

            var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
            settings.TryGetValue("LibraryRootPath", out var libraryRoot);
            settings.TryGetValue("IncomingScanFolder", out var incoming);

            phase("Copia de la base de datos");
            result.DatabaseSnapshot = await BackupService.SnapshotDatabaseAsync(_db, "antes-de-vaciar", keep: 10, cancellationToken: cancellationToken);

            phase("Copia completa de lo tuyo (ZIP)");
            var backupsDir = BackupService.BackupsDir(_db);
            Directory.CreateDirectory(backupsDir);
            result.ZipBackup = Path.Combine(backupsDir, $"antes-de-vaciar-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            await using (var zipFile = new FileStream(result.ZipBackup, FileMode.CreateNew))
            {
                await _backup.WriteZipAsync(zipFile, cancellationToken);
            }

            // Estado y universos para reaplicar tras cada pasada del escáner hasta que aparezcan todos sus libros
            var manifest = await _backup.BuildManifestAsync(cancellationToken);
            result.PendingRestore = BackupService.PendingRestorePath(_db);
            await File.WriteAllTextAsync(result.PendingRestore, JsonSerializer.Serialize(manifest, BackupService.JsonOptions), cancellationToken);

            phase("Caché de metadatos");
            result.MetadataCached = await MetadataCacheService.SnapshotLibraryAsync(_db, progress, cancellationToken);

            phase("Localizando EPUB que solo están en la biblioteca");
            var libraryOnly = new List<string>();
            foreach (var path in await _db.Books.AsNoTracking().Select(b => b.EpubFilePath).ToListAsync(cancellationToken))
            {
                var resolved = LibraryPathResolver.Resolve(path, libraryRoot);
                if (resolved != null && IsUnder(resolved, libraryRoot) && IsRealFile(resolved) && !IsUnder(resolved, incoming)) libraryOnly.Add(resolved);
            }

            phase("Vaciando libros");
            result.BooksDeleted = await _db.Books.CountAsync(cancellationToken);
            await WipeAsync(cancellationToken);

            phase("Restaurando libros en lectura y sin conexión");
            await using (var zipFile = File.OpenRead(result.ZipBackup))
            {
                result.Restore = await _backup.RestoreAsync(zipFile, isZip: true, cancellationToken);
            }

            phase("Reimportando EPUB de la biblioteca");
            int done = 0;
            foreach (var path in libraryOnly.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _db.ChangeTracker.Clear();
                    var book = await _scanner.ScanEpubAsync(path, "GoogleBooks", Path.GetFileName(path), forceMove: true);
                    if (book.Id == 0) _db.Books.Add(book);
                    await _db.SaveChangesAsync(cancellationToken);
                    result.LibraryOnlyImported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.LibraryOnlyErrors++;
                    _logger.LogWarning(ex, "No se pudo reimportar {Path}", path);
                }
                progress(++done);
            }

            phase("Hecho: el escáner reimporta la carpeta de entrada");
            return result;
        }
        finally
        {
            _scanState.Paused = false;
        }
    }

    /// <summary>Borra libros y todo lo que cuelga de ellos. Conserva ajustes, audios huérfanos y caché de metadatos.</summary>
    public async Task WipeAsync(CancellationToken cancellationToken = default)
    {
        await _db.BookAnnotations.ExecuteDeleteAsync(cancellationToken);
        await _db.BookAudioChapters.ExecuteDeleteAsync(cancellationToken);
        await _db.BookAudioTracks.ExecuteDeleteAsync(cancellationToken);
        await _db.SyncMaps.ExecuteDeleteAsync(cancellationToken);
        await _db.BookAuthors.ExecuteDeleteAsync(cancellationToken);
        await _db.BookCategories.ExecuteDeleteAsync(cancellationToken);
        await _db.ScannedSourceFiles.ExecuteDeleteAsync(cancellationToken);
        await _db.Books.ExecuteDeleteAsync(cancellationToken);
        await _db.Series.ExecuteDeleteAsync(cancellationToken);
        await _db.Universes.ExecuteDeleteAsync(cancellationToken);
        await _db.Authors.ExecuteDeleteAsync(cancellationToken);
        await _db.Categories.ExecuteDeleteAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        // Los ids siguen subiendo (AUTOINCREMENT): un cliente con datos viejos nunca escribirá en un libro nuevo
        await SqliteSearchIndex.EnsureUpToDateAsync(_db, forceRebuild: true, cancellationToken);
        await _db.Database.ExecuteSqlRawAsync("VACUUM", cancellationToken);
    }
}
