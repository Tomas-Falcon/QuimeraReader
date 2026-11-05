using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Application.Common;

namespace QuimeraReader.API.BackgroundServices;

public class LibraryScanBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LibraryScanBackgroundService> _logger;
    private readonly LibraryScanState _scanState;
    private readonly HashSet<string> _attemptedFiles = new();

    public LibraryScanBackgroundService(IServiceProvider serviceProvider, ILogger<LibraryScanBackgroundService> logger, LibraryScanState scanState)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _scanState = scanState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LibraryScanBackgroundService is starting.");
        await MigrateLegacyOrphansAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformFullScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing LibraryScanBackgroundService.");
                _scanState.IsScanning = false;
            }

            // Descansar 5 minutos antes de volver a comprobar si hay archivos nuevos
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task PerformFullScanAsync(CancellationToken stoppingToken)
    {
        if (_scanState.Paused) return;

        string scanFolder;
        int batchSize;
        HashSet<string> processedSources;
        HashSet<string> processedAudios;

        // Fase 1: Leer configuración y estado actual de la BD
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IncomingScanFolder", stoppingToken);
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value) || !Directory.Exists(setting.Value))
                return;
            scanFolder = setting.Value;

            var batchSizeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ScanBatchSize", stoppingToken);
            batchSize = int.TryParse(batchSizeSetting?.Value, out int parsedSize) ? parsedSize : 100;

            var sourcesList = await dbContext.Books.Where(b => b.SourceFilePath != null).Select(b => b.SourceFilePath!).ToListAsync(stoppingToken);
            processedSources = new HashSet<string>(sourcesList);
            // También los duplicados ignorados y los EPUB que fallaron: no volver a leerlos en cada escaneo
            processedSources.UnionWith(await dbContext.ScannedSourceFiles.Select(f => f.Path).ToListAsync(stoppingToken));
            
            var audiosList = await dbContext.BookAudioTracks.Where(t => t.FilePath != null).Select(t => t.FilePath!).ToListAsync(stoppingToken);
            processedAudios = new HashSet<string>(audiosList);
        }

        // Fase 2: Escanear disco
        string[] epubExtensions = { ".epub" };
        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav" };
        
        var rawFiles = Directory.GetFiles(scanFolder, "*.*", SearchOption.AllDirectories)
                                .Where(f => epubExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) || audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                .ToArray();

        var pendingFiles = rawFiles.Where(f => !processedSources.Contains(f) && !processedAudios.Contains(f) && !_attemptedFiles.Contains(f)).ToArray();

        if (pendingFiles.Length == 0) return;

        _scanState.IsScanning = true;
        _scanState.TotalFilesFound = pendingFiles.Length;
        _scanState.FilesProcessed = 0;

        _logger.LogInformation($"Iniciando procesamiento de {pendingFiles.Length} archivos en lotes de {batchSize}.");

        try
        {
            // Procesar en lotes creando un Scope nuevo por lote para no saturar el DbContext
            for (int i = 0; i < pendingFiles.Length; i += batchSize)
            {
                if (stoppingToken.IsCancellationRequested || _scanState.Paused) break;

                var batchFiles = pendingFiles.Skip(i).Take(batchSize).ToArray();
                await ProcessBatchFilesAsync(batchFiles, scanFolder, epubExtensions, audioExtensions, stoppingToken);
            }
        }
        finally
        {
            _scanState.IsScanning = false;
            _logger.LogInformation("Escaneo de biblioteca finalizado.");
        }

        if (!_scanState.Paused && !stoppingToken.IsCancellationRequested)
        {
            await ApplyPendingRestoreAsync(stoppingToken);
        }
    }

    private async Task ProcessBatchFilesAsync(string[] files, string scanFolder, string[] epubExtensions, string[] audioExtensions, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scannerService = scope.ServiceProvider.GetRequiredService<EpubScannerService>();
        var audioMatcher = scope.ServiceProvider.GetRequiredService<AudioMatchingService>();
        var mediaPackager = scope.ServiceProvider.GetRequiredService<MediaPackagerService>();

        var modeSetting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "IngestionMode", stoppingToken);
        bool leaveInPlace = modeSetting?.Value == "LeaveInPlace";

        foreach (var file in files)
        {
            if (stoppingToken.IsCancellationRequested || _scanState.Paused) break;
            _scanState.CurrentFile = Path.GetFileName(file);
            // Recordar el archivo durante la vida del proceso: duplicados y EPUB corruptos no se reintentan en bucle
            _attemptedFiles.Add(file);
            
            try
            {
                if (epubExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    var book = await scannerService.ScanEpubAsync(file, "GoogleBooks");
                    if (book.Id == 0)
                    {
                        dbContext.Books.Add(book);
                    }
                    await dbContext.SaveChangesAsync(stoppingToken);
                    await RecordScannedFileAsync(dbContext, file, book.ContentHash, book.Id, scannerService.LastScanOutcome, stoppingToken);
                }
                else if (audioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    var bookId = await audioMatcher.TryMatchAudioToBookAsync(file, stoppingToken);
                    if (bookId.HasValue)
                    {
                        var book = await dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == bookId.Value, stoppingToken);
                        if (book != null)
                        {
                            int nextTrack = book.AudioTracks.Any() ? book.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;
                            string safeTitle = LibraryPathUtils.SafeFileName(book.Title, "audio");
                            string bookDir = Path.GetDirectoryName(string.IsNullOrEmpty(book.EpubFilePath) ? book.CoverImagePath : book.EpubFilePath) ?? scanFolder;
                            Directory.CreateDirectory(bookDir);
                            string newAudioPath = LibraryPathUtils.GetUniqueFilePath(bookDir, $"{safeTitle} track {nextTrack}.mp3");
                            string trackPath = file;

                            // FFmpeg escribe en un temporal que solo se renombra al terminar: si el servidor se reinicia a mitad
                            // de la conversión no queda una pista a medias con nombre definitivo ocupando espacio
                            string partialPath = newAudioPath + ".partial.mp3";
                            try
                            {
                                if (await mediaPackager.NormalizeAudioAsync(file, partialPath))
                                {
                                    File.Move(partialPath, newAudioPath);
                                    trackPath = newAudioPath;
                                    if (!leaveInPlace)
                                    {
                                        try { File.Delete(file); } catch (Exception ex) { _logger.LogWarning(ex, "No se pudo borrar el audio original {File}", file); }
                                    }
                                }
                                else
                                {
                                    // Sin conversión se conserva la extensión real (antes un .m4b acababa copiado con extensión .mp3)
                                    if (File.Exists(partialPath)) File.Delete(partialPath); // posible salida parcial de FFmpeg
                                    trackPath = LibraryPathUtils.GetUniqueFilePath(bookDir, $"{safeTitle} track {nextTrack}{Path.GetExtension(file)}");
                                    File.Copy(file, trackPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "No se pudo copiar el audio {File}; se enlaza en su ubicación original", file);
                                trackPath = file;
                            }

                            book.AudioTracks.Add(new BookAudioTrack { FilePath = trackPath, TrackNumber = nextTrack, FileName = Path.GetFileName(file) });
                            await dbContext.SaveChangesAsync(stoppingToken);
                            // La pista apunta a la copia en la biblioteca, no al original: sin este registro, en modo torrent
                            // el original se volvía a importar (y copiar) tras cada reinicio del servidor
                            await RecordScannedFileAsync(dbContext, file, null, book.Id, "Audio", stoppingToken);
                        }
                    }
                    else
                    {
                        // Los huérfanos van a la biblioteca configurada (volumen persistente), no a /app dentro del contenedor
                        string orphansDir = await GetOrphansDirAsync(dbContext, stoppingToken);
                        Directory.CreateDirectory(orphansDir);
                        string fileName = Path.GetFileName(file);
                        string orphanPath = LibraryPathUtils.GetUniqueFilePath(orphansDir, fileName);

                        if (!leaveInPlace)
                        {
                            File.Move(file, orphanPath);
                        }
                        else
                        {
                            File.Copy(file, orphanPath);
                        }

                        var fileInfo = new FileInfo(orphanPath);
                        dbContext.UnmatchedAudioTracks.Add(new UnmatchedAudioTrack 
                        { 
                            OriginalFileName = fileName, 
                            PhysicalPath = orphanPath, 
                            FileSizeBytes = fileInfo.Length,
                            UploadedAt = DateTime.UtcNow
                        });
                        await dbContext.SaveChangesAsync(stoppingToken);
                        // En modo torrent el original sigue en la carpeta de entrada: registrarlo para no copiarlo otra vez
                        await RecordScannedFileAsync(dbContext, file, null, null, "AudioOrphan", stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error escaneando el archivo {file}");
                if (epubExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    // Un EPUB corrupto no se reintenta en cada arranque
                    dbContext.ChangeTracker.Clear();
                    try { await RecordScannedFileAsync(dbContext, file, null, null, "Error", stoppingToken); } catch { }
                }
            }
            finally
            {
                _scanState.FilesProcessed++;
            }
        }
    }

    /// <summary>
    /// Tras vaciar la biblioteca, el estado del usuario (progreso, sin conexión, notas, universos) queda pendiente en
    /// un JSON y se reaplica después de cada pasada del escáner, a medida que van apareciendo sus libros.
    /// Se da por terminado cuando todos los libros y universos se han encontrado.
    /// </summary>
    private async Task ApplyPendingRestoreAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var path = BackupService.PendingRestorePath(dbContext);
            if (!File.Exists(path)) return;

            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            BackupService.RestoreReport report;
            await using (var stream = File.OpenRead(path))
            {
                report = await backup.RestoreAsync(stream, isZip: false, stoppingToken);
            }

            _logger.LogInformation("Restauración pendiente: {Found} de {Total} libros encontrados, {Pending} universos sin completar",
                report.BooksInBackup - report.NotFound.Count, report.BooksInBackup, report.UniversesPending);

            if (report.NotFound.Count == 0 && report.UniversesPending == 0)
            {
                File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, $"pending-restore.applied-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
                _logger.LogInformation("Restauración pendiente completada.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo aplicar la restauración pendiente");
        }
    }

    private static async Task RecordScannedFileAsync(AppDbContext dbContext, string path, string? hash, int? bookId, string outcome, CancellationToken stoppingToken)
    {
        var entry = await dbContext.ScannedSourceFiles.FirstOrDefaultAsync(f => f.Path == path, stoppingToken);
        if (entry == null) dbContext.ScannedSourceFiles.Add(entry = new ScannedSourceFile { Path = path });
        entry.ContentHash = hash;
        entry.BookId = bookId is > 0 ? bookId : null;
        entry.Outcome = outcome;
        entry.ScannedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private static string LegacyOrphansDir => Path.Combine(Directory.GetCurrentDirectory(), "Library", "Orphans");

    private static async Task<string> GetOrphansDirAsync(AppDbContext dbContext, CancellationToken stoppingToken)
    {
        var libraryRoot = await dbContext.SystemSettings
            .Where(s => s.Key == "LibraryRootPath")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(stoppingToken);
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = QuimeraReader.Application.Common.LibraryPathUtils.DefaultLibraryRoot;
        return Path.Combine(libraryRoot, "Orphans");
    }

    /// <summary>
    /// Versiones anteriores guardaban los huérfanos del escáner en /app/Library/Orphans (fuera de los volúmenes de Docker),
    /// donde se pierden al recrear el contenedor. Si todavía existen, se mueven a la biblioteca configurada.
    /// </summary>
    private async Task MigrateLegacyOrphansAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string legacyDir = Path.GetFullPath(LegacyOrphansDir);
            string targetDir = Path.GetFullPath(await GetOrphansDirAsync(dbContext, stoppingToken));
            if (string.Equals(legacyDir, targetDir, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(legacyDir)) return;

            var legacyPrefix = legacyDir + Path.DirectorySeparatorChar;
            var legacyOrphans = (await dbContext.UnmatchedAudioTracks.ToListAsync(stoppingToken))
                .Where(o => o.PhysicalPath.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (legacyOrphans.Count == 0) return;

            Directory.CreateDirectory(targetDir);
            int moved = 0;
            foreach (var orphan in legacyOrphans)
            {
                if (!File.Exists(orphan.PhysicalPath)) continue;
                var newPath = LibraryPathUtils.GetUniqueFilePath(targetDir, Path.GetFileName(orphan.PhysicalPath));
                File.Move(orphan.PhysicalPath, newPath);
                orphan.PhysicalPath = newPath;
                moved++;
            }
            await dbContext.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Migrados {Count} audios huérfanos de {Legacy} a {Target}.", moved, legacyDir, targetDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrando audios huérfanos desde la ruta antigua.");
        }
    }
}
