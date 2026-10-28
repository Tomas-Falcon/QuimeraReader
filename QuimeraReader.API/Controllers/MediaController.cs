using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/media")]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly MediaPackagerService _packagerService;

    public MediaController(AppDbContext dbContext, MediaPackagerService packagerService)
    {
        _dbContext = dbContext;
        _packagerService = packagerService;
    }

    [HttpGet("books/{id}/package")]
    public async Task<IActionResult> GetPackage(int id, [FromQuery] string format)
    {
        var book = await _dbContext.Books
            .Include(b => b.AudioTracks)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();

        if (format == "audiobook" || format == "readaloud")
        {
            var zipStream = await _packagerService.CreateAudiobookPackageAsync(book);
            return File(zipStream, "application/zip", $"{book.Title}.drb");
        }
        
        var resolvedEpub = await GetResolvedPathAsync(book.EpubFilePath);
        if (resolvedEpub == null) return NotFound();

        return PhysicalFile(resolvedEpub, "application/epub+zip", enableRangeProcessing: true);
    }

    private async Task<string?> GetResolvedPathAsync(string? originalPath)
    {
        if (string.IsNullOrEmpty(originalPath)) return null;

        // Si el archivo existe tal cual, lo devolvemos (ej. entorno local)
        if (System.IO.File.Exists(originalPath)) return originalPath;

        // Si no existe, podría ser un problema de cruce de SO (Windows DB vs Linux Docker)
        // Intentamos reconstruir la ruta relativa a la carpeta de la biblioteca actual
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LibraryRootPath");
        string libraryRoot = setting?.Value ?? "/media";

        var parts = originalPath.Replace("\\", "/").Split('/', System.StringSplitOptions.RemoveEmptyEntries);

        // Probar con las últimas 4 partes (Autor/Saga/Libro/Archivo) y 3 partes (Autor/Libro/Archivo)
        for (int depth = 4; depth >= 2; depth--)
        {
            if (parts.Length >= depth)
            {
                var relativeParts = parts.Skip(parts.Length - depth).ToArray();
                var candidatePath = Path.Combine(new[] { libraryRoot }.Concat(relativeParts).ToArray());
                if (System.IO.File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    [HttpGet("books/{id}/cover")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetCover(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        if (!string.IsNullOrEmpty(book.CoverImagePath) && book.CoverImagePath.StartsWith("http"))
        {
            return Redirect(book.CoverImagePath);
        }

        var resolvedPath = await GetResolvedPathAsync(book.CoverImagePath);
        if (resolvedPath == null) return NotFound();

        var mimeType = GetMimeType(resolvedPath);
        return PhysicalFile(resolvedPath, mimeType);
    }

    [HttpGet("books/{id}/file.epub")]
    public async Task<IActionResult> GetEpub(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        var resolvedPath = await GetResolvedPathAsync(book.EpubFilePath);
        if (resolvedPath == null) return NotFound();

        return PhysicalFile(resolvedPath, "application/epub+zip", enableRangeProcessing: true);
    }

    [HttpGet("books/{id}/tracks/{trackNumber}")]
    public async Task<IActionResult> GetAudioTrack(int id, int trackNumber)
    {
        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();

        var audioTrack = book.AudioTracks.FirstOrDefault(t => t.TrackNumber == trackNumber);
        var audioFilePath = audioTrack?.FilePath;

        var resolvedPath = await GetResolvedPathAsync(audioFilePath);
        if (resolvedPath == null) return NotFound();

        var mimeType = GetMimeType(resolvedPath);
        return PhysicalFile(resolvedPath, mimeType, enableRangeProcessing: true);
    }

    [HttpGet("books/{id}/audio")]
    public async Task<IActionResult> GetAudio(int id)
    {
        var book = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == id);
        if (book == null) return NotFound();

        var audioTrack = book.AudioTracks.OrderBy(t => t.TrackNumber).FirstOrDefault();
        var audioFilePath = audioTrack?.FilePath;

        var resolvedPath = await GetResolvedPathAsync(audioFilePath);
        if (resolvedPath == null) return NotFound();

        var mimeType = GetMimeType(resolvedPath);
        // enableRangeProcessing = true es crucial para el streaming de audio (206 Partial Content)
        return PhysicalFile(resolvedPath, mimeType, enableRangeProcessing: true);
    }

    private string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4b" or ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };
    }
}
