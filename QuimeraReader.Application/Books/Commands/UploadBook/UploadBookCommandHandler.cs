using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Books.Commands.UploadBook;

public class UploadBookCommandHandler : IRequestHandler<UploadBookCommand, UploadBookResultDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IAudioMatchingService _audioMatcher;
    private readonly IEpubScannerService _scannerService;

    public UploadBookCommandHandler(IAppDbContext dbContext, IAudioMatchingService audioMatcher, IEpubScannerService scannerService)
    {
        _dbContext = dbContext;
        _audioMatcher = audioMatcher;
        _scannerService = scannerService;
    }

    public async Task<UploadBookResultDto> Handle(UploadBookCommand request, CancellationToken cancellationToken)
    {
        if (request.FileStream == null || request.Length == 0)
            throw new Exception("No se proporcionó ningún archivo.");

        string[] audioExtensions = { ".mp3", ".m4b", ".m4a", ".wav", ".ogg" };
        bool isAudio = audioExtensions.Contains(Path.GetExtension(request.FileName).ToLowerInvariant());

        if (!request.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) && !isAudio)
            throw new Exception("Solo se permiten archivos .epub o audios compatibles.");

        string baseTemp = Path.GetTempFileName();
        string tempPath = baseTemp + Path.GetExtension(request.FileName);
        
        using (var fs = new FileStream(tempPath, FileMode.Create))
        {
            await request.FileStream.CopyToAsync(fs, cancellationToken);
        }

        if (isAudio)
        {
            var matchedBookId = await _audioMatcher.TryMatchAudioToBookAsync(tempPath);
            if (matchedBookId.HasValue)
            {
                var matchedBook = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == matchedBookId.Value, cancellationToken);
                if (matchedBook != null)
                {
                    string outDir = await LibraryPathUtils.GetBookOutputDirAsync(matchedBook, _dbContext);
                    if (!string.IsNullOrEmpty(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                        string newAudioPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(matchedBook.EpubFilePath ?? matchedBook.Title) + "_audio" + Path.GetExtension(request.FileName));
                        File.Move(tempPath, newAudioPath, true);
                        matchedBook.AudioTracks.Clear();
                        matchedBook.AudioTracks.Add(new BookAudioTrack { FilePath = newAudioPath, TrackNumber = 1 });
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        return new UploadBookResultDto { Message = "Audio emparejado con libro existente: " + matchedBook.Title, HasAudio = true };
                    }
                }
            }
            var settingsDict = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
            settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
            if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library");

            string orphansDir = Path.Combine(libraryRoot, "Orphans");
            Directory.CreateDirectory(orphansDir);
            string orphanPath = Path.Combine(orphansDir, request.FileName);
            File.Move(tempPath, orphanPath, true);
            _dbContext.UnmatchedAudioTracks.Add(new UnmatchedAudioTrack { OriginalFileName = request.FileName, PhysicalPath = orphanPath, FileSizeBytes = request.Length, UploadedAt = DateTime.UtcNow });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new UploadBookResultDto { Message = "El audio fue guardado como huérfano porque no se encontró coincidencia." };
        }

        var book = await _scannerService.ScanEpubAsync(tempPath, "GoogleBooks", request.FileName, forceMove: true);
        
        if (book.Id == 0)
        {
            _dbContext.Books.Add(book);
        }
        
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UploadBookResultDto
        {
            Id = book.Id,
            Title = book.Title,
            HasEpub = !string.IsNullOrEmpty(book.EpubFilePath),
            HasCover = !string.IsNullOrEmpty(book.CoverImagePath),
            IsAvailableOffline = book.IsAvailableOffline,
            HasAudio = book.AudioTracks != null && book.AudioTracks.Any(),
            Message = "Libro subido correctamente."
        };
    }
}
