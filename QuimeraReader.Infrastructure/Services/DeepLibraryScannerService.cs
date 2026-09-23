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

            _logger.LogInformation("Deep Scan procesando audio huérfano: {Path}", audioPath);

            var bookId = await _audioMatchingService.TryMatchAudioToBookAsync(audioPath, cancellationToken);
            
            if (bookId.HasValue)
            {
                var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == bookId.Value, cancellationToken);
                if (book != null)
                {
                    int nextTrack = book.AudioTracks.Any() ? book.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;
                    
                    string directory = Path.GetDirectoryName(audioPath) ?? string.Empty;
                    string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
                    
                    string newAudioPath = Path.Combine(directory, $"{safeTitle} track {nextTrack}.mp3");

                    if (audioPath != newAudioPath)
                    {
                        try 
                        {
                            _logger.LogInformation($"Normalizando audio huérfano a MP3: {audioPath}");
                            bool converted = await _mediaPackager.NormalizeAudioAsync(audioPath, newAudioPath);

                            if (converted)
                            {
                                try { File.Delete(audioPath); } catch { }
                                book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack });
                                _logger.LogInformation("Asignado Track {Track} normalizado al libro '{Title}': {Path}", nextTrack, book.Title, newAudioPath);
                            }
                            else
                            {
                                File.Move(audioPath, newAudioPath, true);
                                book.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = nextTrack });
                            }
                        } 
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error al renombrar o normalizar el audio colocado manualmente.");
                            book.AudioTracks.Add(new BookAudioTrack { FilePath = audioPath, TrackNumber = nextTrack });
                        }
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}