using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.BackgroundServices;

/// <summary>Ejecuta <see cref="LibraryFileAudit"/> fuera de la petición HTTP, de una en una, y guarda el último resultado en memoria.</summary>
public class MissingFilesAuditRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MissingFilesAuditRunner> _logger;
    private readonly object _lock = new();

    private bool _running;
    private bool _apply;
    private DateTime? _startedAt;
    private DateTime? _finishedAt;
    private int _filesIndexed;
    private string? _error;
    private LibraryFileAudit.Report? _report;

    public MissingFilesAuditRunner(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime, ILogger<MissingFilesAuditRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    public object GetStatus()
    {
        lock (_lock)
        {
            return new { Running = _running, Apply = _apply, StartedAt = _startedAt, FinishedAt = _finishedAt, FilesIndexed = _filesIndexed, Error = _error, Report = _report };
        }
    }

    public bool TryStart(bool apply)
    {
        lock (_lock)
        {
            if (_running) return false;
            _running = true;
            _apply = apply;
            _startedAt = DateTime.UtcNow;
            _finishedAt = null;
            _filesIndexed = 0;
            _error = null;
        }

        _ = Task.Run(() => RunAsync(apply, _lifetime.ApplicationStopping));
        return true;
    }

    private async Task RunAsync(bool apply, CancellationToken cancellationToken)
    {
        LibraryFileAudit.Report? report = null;
        string? error = null;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
            settings.TryGetValue("LibraryRootPath", out var libraryRoot);
            settings.TryGetValue("IncomingScanFolder", out var incoming);

            report = await LibraryFileAudit.RunAsync(db, libraryRoot, new[] { libraryRoot, incoming, "/library", "/media" }!, apply, cancellationToken,
                seen => { lock (_lock) _filesIndexed = seen; });
            _logger.LogInformation("Revisión de archivos: {Missing} EPUB sin archivo, {Found} reencontrados, {Repaired} reparados",
                report.EpubMissing, report.EpubFoundElsewhere, report.Repaired);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Error en la revisión de archivos que faltan");
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
                _finishedAt = DateTime.UtcNow;
                _error = error;
                if (report != null) _report = report;
            }
        }
    }
}
