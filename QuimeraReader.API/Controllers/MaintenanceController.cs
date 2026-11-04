using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/maintenance")]
public class MaintenanceController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public MaintenanceController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Informe de libros cuyo EPUB o portada no existe, agrupado por carpeta. Con apply=true guarda
    /// las rutas reencontradas por nombre de archivo en la biblioteca, la carpeta de entrada, /library y /media.
    /// </summary>
    [HttpGet("missing-files")]
    public Task<IActionResult> Report(CancellationToken cancellationToken) => RunAsync(false, cancellationToken);

    [HttpPost("missing-files/repair")]
    public Task<IActionResult> Repair(CancellationToken cancellationToken) => RunAsync(true, cancellationToken);

    private async Task<IActionResult> RunAsync(bool apply, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);
        settings.TryGetValue("IncomingScanFolder", out var incoming);

        var report = await LibraryFileAudit.RunAsync(_dbContext, libraryRoot, new[] { libraryRoot, incoming, "/library", "/media" }!, apply, cancellationToken);
        return Ok(report);
    }
}
