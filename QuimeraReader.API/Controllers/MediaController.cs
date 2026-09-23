using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;
using System.IO;
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
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        if (format == "audiobook" || format == "readaloud")
        {
            var zipStream = await _packagerService.CreateAudiobookPackageAsync(book);
            return File(zipStream, "application/zip", $"{book.Title}.drb");
        }
        
        // Si es ebook puro, devolvemos el epub tal cual
        return PhysicalFile(book.EpubFilePath, "application/epub+zip", enableRangeProcessing: true);
    }

    private async Task<string?> GetResolvedPathAsync(string? originalPath)
    {
        if (string.IsNullOrEmpty(originalPath)) return null;

        // Si el archivo existe tal cual, lo devolvemos (ej. entorno local)
        if (System.IO.File.Exists(originalPath)) return originalPath;

        // Si no existe, podría ser un problema de cruce de SO (Windows DB vs Linux Docker)
        // Intentamos reconstruir la ruta relativa a la carpeta de la biblioteca actual
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "LibraryRootPath");
        string libraryRoot = setting?.Value ?? "/media"; // fallback para docker

        // Extraer el nombre del autor y libro de la ruta original
        // Ej: C:\Users\tomas\...\Library\Author\Title\cover.jpg -> Author/Title/cover.jpg
        var parts = originalPath.Replace("\\", "/").Split('/');
        
        // Normalmente la estructura es LibraryRoot/Author/BookDir/file
        // Vamos a buscar la última carpeta que coincida con el autor del libro...
        // Una forma más segura es intentar recuperar las últimas 3 partes (Autor/Libro/Archivo)
        if (parts.Length >= 3)
        {
            var relativeParts = parts.Skip(parts.Length - 3).ToArray();
            var newPath = Path.Combine(libraryRoot, relativeParts[0], relativeParts[1], relativeParts[2]);
            if (System.IO.File.Exists(newPath))
            {
                return newPath;
            }
        }
        
        // Si todo falla
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

        return PhysicalFile(resolvedPath, "audio/mpeg", enableRangeProcessing: true);
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
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4b" or ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };
    }
}
