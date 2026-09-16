using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using System.IO;
using System.Threading.Tasks;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public MediaController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("books/{id}/cover")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetCover(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null || string.IsNullOrEmpty(book.CoverImagePath) || !System.IO.File.Exists(book.CoverImagePath))
            return NotFound();

        var mimeType = GetMimeType(book.CoverImagePath);
        return PhysicalFile(book.CoverImagePath, mimeType);
    }

    [HttpGet("books/{id}/epub")]
    public async Task<IActionResult> GetEpub(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null || string.IsNullOrEmpty(book.EpubFilePath) || !System.IO.File.Exists(book.EpubFilePath))
            return NotFound();

        return PhysicalFile(book.EpubFilePath, "application/epub+zip", enableRangeProcessing: true);
    }

    [HttpGet("books/{id}/audio")]
    public async Task<IActionResult> GetAudio(int id)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null || string.IsNullOrEmpty(book.AudioFilePath) || !System.IO.File.Exists(book.AudioFilePath))
            return NotFound();

        var mimeType = GetMimeType(book.AudioFilePath);
        // enableRangeProcessing = true es crucial para el streaming de audio (206 Partial Content)
        return PhysicalFile(book.AudioFilePath, mimeType, enableRangeProcessing: true);
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
