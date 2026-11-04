using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.API.BackgroundServices;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/maintenance")]
public class MaintenanceController : ControllerBase
{
    private const string MissingFilesJob = "missing-files";
    private const string DuplicatesJob = "duplicates";

    private readonly MaintenanceJobRunner _runner;
    private readonly AppDbContext _dbContext;

    public MaintenanceController(MaintenanceJobRunner runner, AppDbContext dbContext)
    {
        _runner = runner;
        _dbContext = dbContext;
    }

    private static async Task<(string? LibraryRoot, string? Incoming)> GetFoldersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        settings.TryGetValue("LibraryRootPath", out var libraryRoot);
        settings.TryGetValue("IncomingScanFolder", out var incoming);
        return (libraryRoot, incoming);
    }

    /// <summary>Estado de la revisión de archivos que faltan y el último informe.</summary>
    [HttpGet("missing-files")]
    public IActionResult MissingFilesStatus() => Ok(_runner.GetStatus(MissingFilesJob));

    /// <summary>
    /// Lanza en segundo plano la revisión de libros cuyo EPUB o portada no existe (recorrer /media puede tardar mucho).
    /// Con apply=true guarda las rutas reencontradas por nombre de archivo.
    /// </summary>
    [HttpPost("missing-files/run")]
    public IActionResult RunMissingFiles([FromQuery] bool apply = false)
    {
        var started = _runner.TryStart(MissingFilesJob, new { apply }, async (services, progress, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var (libraryRoot, incoming) = await GetFoldersAsync(db, token);
            return await LibraryFileAudit.RunAsync(db, libraryRoot, new[] { libraryRoot, incoming, "/library", "/media" }!, apply, token, progress);
        });
        return started ? Accepted(_runner.GetStatus(MissingFilesJob)) : Conflict(_runner.GetStatus(MissingFilesJob));
    }

    private const string CoversJob = "covers";

    /// <summary>Estado de la reparación de portadas y su último informe.</summary>
    [HttpGet("covers")]
    public IActionResult CoversStatus() => Ok(_runner.GetStatus(CoversJob));

    /// <summary>
    /// Repara las portadas que no existen en disco: del EPUB y, con online=true, de OpenLibrary/Google Books.
    /// Siempre quedan guardadas en la biblioteca.
    /// </summary>
    [HttpPost("covers/run")]
    public IActionResult RunCovers([FromQuery] bool online = true)
    {
        var started = _runner.TryStart(CoversJob, new { online }, async (services, progress, token) =>
            await services.GetRequiredService<CoverRepairService>().RepairAllAsync(online, progress, token));
        return started ? Accepted(_runner.GetStatus(CoversJob)) : Conflict(_runner.GetStatus(CoversJob));
    }

    /// <summary>Estado de la limpieza de duplicados y su último informe.</summary>
    [HttpGet("duplicates")]
    public IActionResult DuplicatesStatus() => Ok(_runner.GetStatus(DuplicatesJob));

    /// <summary>
    /// Une libros repetidos (mismo hash, o sin archivo con una copia del mismo título y autor) conservando
    /// lo que hizo el usuario. Sin apply solo informa. deleteOrphans borra además los libros sin archivo, sin copia y sin datos del usuario.
    /// </summary>
    [HttpPost("duplicates/run")]
    public IActionResult RunDuplicates([FromQuery] bool apply = false, [FromQuery] bool deleteOrphans = false)
    {
        var started = _runner.TryStart(DuplicatesJob, new { apply, deleteOrphans }, async (services, progress, token) =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var (libraryRoot, _) = await GetFoldersAsync(db, token);
            if (apply) await BackupService.SnapshotDatabaseAsync(db, "antes-de-unificar", cancellationToken: token);
            return await BookDeduplicator.RunAsync(db, libraryRoot, apply, deleteOrphans, token);
        });
        return started ? Accepted(_runner.GetStatus(DuplicatesJob)) : Conflict(_runner.GetStatus(DuplicatesJob));
    }

    /// <summary>
    /// Copia de seguridad de los libros con algo del usuario. format=json: solo estado (KB). format=zip: además
    /// EPUB, audios, portadas y sincronización de esos libros, para recuperarlos aunque se pierdan los archivos.
    /// </summary>
    [HttpGet("backup")]
    public async Task<IActionResult> Backup([FromServices] BackupService backup, [FromQuery] string format = "json", CancellationToken cancellationToken = default)
    {
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmm");
        if (!string.Equals(format, "zip", StringComparison.OrdinalIgnoreCase))
        {
            var manifest = await backup.BuildManifestAsync(cancellationToken);
            var json = JsonSerializer.SerializeToUtf8Bytes(manifest, BackupService.JsonOptions);
            return File(json, "application/json", $"quimera-estado-{stamp}.json");
        }

        // ZipArchive escribe de forma síncrona: se genera en un temporal que se borra al cerrar la descarga
        var tempPath = Path.Combine(Path.GetTempPath(), $"quimera-backup-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        try
        {
            await backup.WriteZipAsync(stream, cancellationToken);
            stream.Position = 0;
            return File(stream, "application/zip", $"quimera-copia-{stamp}.zip");
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>Restaura una copia (.json o .zip). Lo que ya existe se completa; con ZIP, lo que falte se importa.</summary>
    [HttpPost("backup/restore")]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Restore([FromServices] BackupService backup, IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequest(new { Error = "No se recibió ningún archivo." });
        bool isZip = file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        await BackupService.SnapshotDatabaseAsync(_dbContext, "antes-de-restaurar", cancellationToken: cancellationToken);

        var tempPath = Path.Combine(Path.GetTempPath(), $"quimera-restore-{Guid.NewGuid():N}");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew))
            {
                await file.CopyToAsync(target, cancellationToken);
            }
            await using var source = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
            var report = await backup.RestoreAsync(source, isZip, cancellationToken);
            return Ok(report);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (JsonException)
        {
            return BadRequest(new { Error = "El archivo no es una copia de seguridad válida." });
        }
        finally
        {
            try { System.IO.File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>Copia de la base de datos en /config/backups (se guardan las 5 últimas).</summary>
    [HttpPost("db-snapshot")]
    public async Task<IActionResult> DatabaseSnapshot(CancellationToken cancellationToken) =>
        Ok(new { Path = await BackupService.SnapshotDatabaseAsync(_dbContext, "manual", cancellationToken: cancellationToken) });
}
