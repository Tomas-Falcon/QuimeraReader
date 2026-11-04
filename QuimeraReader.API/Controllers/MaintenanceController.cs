using Microsoft.AspNetCore.Mvc;
using QuimeraReader.API.BackgroundServices;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/maintenance")]
public class MaintenanceController : ControllerBase
{
    private readonly MissingFilesAuditRunner _runner;

    public MaintenanceController(MissingFilesAuditRunner runner)
    {
        _runner = runner;
    }

    /// <summary>Estado de la revisión de archivos que faltan y el último informe.</summary>
    [HttpGet("missing-files")]
    public IActionResult Status() => Ok(_runner.GetStatus());

    /// <summary>
    /// Lanza en segundo plano la revisión de libros cuyo EPUB o portada no existe (recorrer /media puede tardar mucho).
    /// Con apply=true guarda las rutas reencontradas por nombre de archivo.
    /// </summary>
    [HttpPost("missing-files/run")]
    public IActionResult Run([FromQuery] bool apply = false) =>
        _runner.TryStart(apply) ? Accepted(_runner.GetStatus()) : Conflict(_runner.GetStatus());
}
