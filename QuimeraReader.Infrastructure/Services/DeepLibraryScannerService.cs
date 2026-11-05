using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

public class DeepLibraryScannerService
{
    private readonly AppDbContext _dbContext;
    private readonly AudioMatchingService _audioMatchingService;
    private readonly MediaPackagerService _mediaPackager;
    private readonly ILogger<DeepLibraryScannerService> _logger;

    public DeepLibraryScannerService(AppDbContext dbContext, AudioMatchingService audioMatchingService, MediaPackagerService mediaPackager, ILogger<DeepLibraryScannerService> logger)
    {
        _dbContext = dbContext;
        _audioMatchingService = audioMatchingService;
        _mediaPackager = mediaPackager;
        _logger = logger;
    }

    public async Task ScanLibraryForOrphanAudioAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LibraryRootPath", cancellationToken);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value) || !Directory.Exists(setting.Value))
        {
            _logger.LogWarning("No se configuró la carpeta de librería para hacer Deep Scan.");
            return;
        }

        string[] audioExtensions = { ".mp3", ".m4a", ".m4b", ".wav", ".ogg" };
        var allAudioFiles = Directory.GetFiles(setting.Value, "*.*", SearchOption.AllDirectories)
                                     .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                     .ToList();

        foreach (var audioPath in allAudioFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Check if this audio is already assigned to ANY book
            bool alreadyAssigned = await _dbContext.BookAudioTracks.AnyAsync(t => t.FilePath == audioPath, cancellationToken);
            if (alreadyAssigned) continue;

            // Si es un huérfano registrado, su nombre original identifica el archivo de origen. En modo torrent el escáner
            // copia el audio y puede quedar a la vez como pista de un libro y como huérfano: no volver a añadirlo como
            // otra pista (antes cada noche se sumaba una pista repetida)
            var orphan = await _dbContext.UnmatchedAudioTracks.FirstOrDefaultAsync(u => u.PhysicalPath == audioPath, cancellationToken);
            string originalName = orphan?.OriginalFileName ?? Path.GetFileName(audioPath);
            if (await _dbContext.BookAudioTracks.AnyAsync(t => t.FileName == originalName, cancellationToken))
            {
                _logger.LogInformation("Deep Scan: {Path} es una copia de una pista ya asignada; se deja como está", audioPath);
                continue;
            }

            _logger.LogInformation("Deep Scan procesando audio huérfano: {Path}", audioPath);

            var bookId = await _audioMatchingService.TryMatchAudioToBookAsync(audioPath, cancellationToken);
            
            if (bookId.HasValue)
            {
                var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == bookId.Value, cancellationToken);
                if (book != null)
                {
                    int nextTrack = book.AudioTracks.Any() ? book.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;

                    // El audio asignado va a la carpeta de su libro (no se queda en la de huérfanos)
                    string directory = Path.GetDirectoryName(string.IsNullOrEmpty(book.EpubFilePath) ? book.CoverImagePath : book.EpubFilePath)
                                       ?? Path.GetDirectoryName(audioPath) ?? string.Empty;
                    string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
                    
                    Directory.CreateDirectory(directory);
                    // Nombre único: nunca pisar una pista existente del libro
                    string newAudioPath = QuimeraReader.Application.Common.LibraryPathUtils.GetUniqueFilePath(directory, $"{safeTitle} track {nextTrack}.mp3");

                    if (audioPath != newAudioPath)
                    {
                        try 
                        {
                            _logger.LogInformation($"Normalizando audio huérfano a MP3: {audioPath}");
                            bool converted = await _mediaPackager.NormalizeAudioAsync(audioPath, newAudioPath);

                            if (converted)
                            {
                                try { File.Delete(audioPath); } catch { }
                                book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack, FileName = originalName });
                                _logger.LogInformation("Asignado Track {Track} normalizado al libro '{Title}': {Path}", nextTrack, book.Title, newAudioPath);
                            }
                            else
                            {
                                File.Move(audioPath, newAudioPath, true);
                                book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack, FileName = originalName });
                            }
                        } 
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error al renombrar o normalizar el audio colocado manualmente.");
                            book.AudioTracks.Add(new BookAudioTrack { FilePath = audioPath, TrackNumber = nextTrack, FileName = originalName });
                        }
                    }
                    else
                    {
                        book.AudioTracks.Add(new BookAudioTrack { FilePath = audioPath, TrackNumber = nextTrack, FileName = originalName });
                    }

                    // Ya no es huérfano: sin esto quedaba en la bandeja apuntando a un archivo borrado
                    if (orphan != null) _dbContext.UnmatchedAudioTracks.Remove(orphan);

                    if (book.ProcessingStatus != "SYNCED" && book.ProcessingStatus != "PROCESSING")
                    {
                        book.ProcessingStatus = "PENDING_SYNC";
                    }
                }
            }
            else
            {
                // Si no se encontró libro con certeza suficiente, registrar en UnmatchedAudioTracks si no existe ya
                bool alreadyInUnmatched = await _dbContext.UnmatchedAudioTracks.AnyAsync(u => u.PhysicalPath == audioPath, cancellationToken);
                if (!alreadyInUnmatched)
                {
                    var fileInfo = new FileInfo(audioPath);
                    _dbContext.UnmatchedAudioTracks.Add(new UnmatchedAudioTrack
                    {
                        OriginalFileName = Path.GetFileName(audioPath),
                        PhysicalPath = audioPath,
                        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        UploadedAt = DateTime.UtcNow
                    });
                    _logger.LogInformation("Audio sin coincidencia segura guardado en bandeja de huérfanos: {Path}", audioPath);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
