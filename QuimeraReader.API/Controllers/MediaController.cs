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
        if (System.IO.File.Exists(originalPath)) return originalPath;
        return LibraryPathResolver.Resolve(originalPath, await GetLibraryRootAsync());
    }

    private Task<string?> GetLibraryRootAsync() =>
        _dbContext.SystemSettings.Where(s => s.Key == "LibraryRootPath").Select(s => s.Value).FirstOrDefaultAsync();

    /// <summary>La portada que trae el propio EPUB, para ofrecerla en el editor aunque se haya reemplazado.</summary>
    [HttpGet("books/{id}/epub-cover")]
    public async Task<IActionResult> GetEpubCover(int id, [FromServices] CoverSearchService coverSearch)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        var bytes = await coverSearch.ReadEpubCoverAsync(book);
        if (!CoverRepairService.IsImage(bytes)) return NotFound();
        var mime = CoverRepairService.ImageExtension(bytes!) switch { ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg" };
        return File(bytes!, mime);
    }

    [HttpGet("books/{id}/cover")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetCover(int id, [FromServices] CoverRepairService coverRepair)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        var resolvedPath = await GetResolvedPathAsync(book.CoverImagePath);
        if (resolvedPath == null)
        {
            // Portada perdida o remota: se saca del EPUB y se guarda en la biblioteca para la próxima vez
            if (await coverRepair.RepairAsync(book, await GetLibraryRootAsync(), online: false) != CoverRepairService.Source.None)
            {
                await _dbContext.SaveChangesAsync();
                resolvedPath = book.CoverImagePath;
            }
            else if (!string.IsNullOrEmpty(book.CoverImagePath) && book.CoverImagePath.StartsWith("http"))
            {
                return Redirect(book.CoverImagePath);
            }
        }
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

    // Escucha previa de una pista ya asignada por su Id (pantalla "Administrar Audios"): no toca el progreso del libro
    [HttpGet("audio-tracks/{trackId}")]
    public async Task<IActionResult> GetAudioTrackById(int trackId)
    {
        var track = await _dbContext.BookAudioTracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId);
        if (track == null) return NotFound();

        var resolvedPath = await GetResolvedPathAsync(track.FilePath);
        if (resolvedPath == null) return NotFound();

        return PhysicalFile(resolvedPath, GetMimeType(resolvedPath), enableRangeProcessing: true);
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
