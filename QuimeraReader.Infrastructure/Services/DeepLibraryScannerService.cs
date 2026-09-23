using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;

namespace QuimeraReader.Infrastructure.Services;

public class DeepLibraryScannerService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DeepLibraryScannerService> _logger;

    public DeepLibraryScannerService(AppDbContext dbContext, ILogger<DeepLibraryScannerService> logger)
    {
        _dbContext = dbContext;
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

        var books = await _dbContext.Books.ToListAsync(cancellationToken);
        string[] audioExtensions = { ".mp3", ".m4a", ".m4b", ".wav", ".ogg" };

        var allAudioFiles = Directory.GetFiles(setting.Value, "*.*", SearchOption.AllDirectories)
                                     .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                     .ToList();

        foreach (var audioPath in allAudioFiles)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string directory = Path.GetDirectoryName(audioPath) ?? string.Empty;

            var matchingBook = books.FirstOrDefault(b => 
                !string.IsNullOrEmpty(b.EpubFilePath) && 
                Path.GetDirectoryName(b.EpubFilePath) == directory);

            if (matchingBook != null && !matchingBook.AudioTracks.Any(t => t.FilePath == audioPath))
            {
                _logger.LogInformation("Deep Scan encontró un audio manualmente colocado para el libro {Id}: {Path}", matchingBook.Id, audioPath);
                
                string ext = Path.GetExtension(audioPath);
                string newAudioPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(matchingBook.EpubFilePath) + "_audio" + ext);

                if (audioPath != newAudioPath)
                {
                    try 
                    {
                        File.Move(audioPath, newAudioPath, true);
                        matchingBook.AudioTracks.Clear();
                        matchingBook.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = 1 });
                    } 
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al renombrar el audio colocado manualmente.");
                        matchingBook.AudioTracks.Clear();
                        matchingBook.AudioTracks.Add(new BookAudioTrack { FilePath = audioPath, TrackNumber = 1 });
                    }
                }
                else
                {
                    matchingBook.AudioTracks.Clear();
                    matchingBook.AudioTracks.Add(new BookAudioTrack { FilePath = audioPath, TrackNumber = 1 });
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
