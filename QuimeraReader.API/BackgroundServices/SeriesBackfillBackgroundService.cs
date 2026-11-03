using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.BackgroundServices;

/// <summary>
/// Rellena una sola vez la saga de los libros ya importados leyendo el OPF de su EPUB (antes el escáner no la
/// leía). Va por lotes, guarda el progreso (se reanuda tras un reinicio) y pausa entre lotes para no acaparar SQLite.
/// </summary>
public class SeriesBackfillBackgroundService : BackgroundService
{
    public const string VersionKey = "SeriesBackfillVersion";
    public const string LastIdKey = "SeriesBackfillLastId";
    public const string StatsKey = "SeriesBackfillStats";
    private const string CurrentVersion = "1";
    private const int BatchSize = 200;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SeriesBackfillBackgroundService> _logger;

    public SeriesBackfillBackgroundService(IServiceProvider serviceProvider, ILogger<SeriesBackfillBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public class Stats
    {
        public int Scanned { get; set; }
        public int WithSeries { get; set; }
        public int MissingFile { get; set; }
        public int Errors { get; set; }
        public bool Done { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Dejar que el arranque (migraciones, índices de búsqueda) termine antes de empezar
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); } catch (TaskCanceledException) { return; }

        try
        {
            await RunAsync(stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var merged = await SeriesMaintenance.MergeCaseDuplicatesAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), stoppingToken);
            if (merged > 0) _logger.LogInformation("Fusionadas {Count} sagas duplicadas por mayúsculas", merged);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en el relleno de sagas; se reintentará en el próximo arranque.");
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        int lastId;
        Stats stats;
        string? libraryRoot;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, stoppingToken);
            if (settings.TryGetValue(VersionKey, out var version) && version == CurrentVersion) return;

            lastId = settings.TryGetValue(LastIdKey, out var last) && int.TryParse(last, out var parsed) ? parsed : 0;
            stats = settings.TryGetValue(StatsKey, out var json) && !string.IsNullOrEmpty(json)
                ? JsonSerializer.Deserialize<Stats>(json) ?? new Stats()
                : new Stats();
            libraryRoot = settings.TryGetValue("LibraryRootPath", out var root) ? root : null;
        }

        _logger.LogInformation("Relleno de sagas: empezando desde el libro {LastId}", lastId);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var batch = await db.Books
                .Where(b => b.Id > lastId && b.SeriesId == null && b.EpubFilePath != null && b.EpubFilePath != "")
                .OrderBy(b => b.Id)
                .Take(BatchSize)
                .ToListAsync(stoppingToken);

            if (batch.Count == 0)
            {
                stats.Done = true;
                await SaveProgressAsync(db, lastId, stats, done: true, stoppingToken);
                _logger.LogInformation("Relleno de sagas terminado: {Scanned} libros revisados, {WithSeries} con saga, {Missing} sin archivo, {Errors} errores",
                    stats.Scanned, stats.WithSeries, stats.MissingFile, stats.Errors);
                return;
            }

            foreach (var book in batch)
            {
                stats.Scanned++;
                var path = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);
                if (path == null)
                {
                    stats.MissingFile++;
                    continue;
                }

                try
                {
                    var info = EpubOpfReader.ReadSeries(path);
                    if (info != null && await SeriesAssigner.ApplyAsync(db, book, info, stoppingToken)) stats.WithSeries++;
                }
                catch (Exception ex)
                {
                    stats.Errors++;
                    _logger.LogDebug(ex, "No se pudo leer la saga del EPUB {Path}", path);
                }
            }

            lastId = batch[^1].Id;
            await db.SaveChangesAsync(stoppingToken);
            await SaveProgressAsync(db, lastId, stats, done: false, stoppingToken);

            if (stats.Scanned % 5000 < BatchSize)
            {
                _logger.LogInformation("Relleno de sagas: {Scanned} libros revisados, {WithSeries} con saga (último id {LastId})", stats.Scanned, stats.WithSeries, lastId);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }
    }

    private static async Task SaveProgressAsync(AppDbContext db, int lastId, Stats stats, bool done, CancellationToken token)
    {
        await SetAsync(db, LastIdKey, lastId.ToString(), token);
        await SetAsync(db, StatsKey, JsonSerializer.Serialize(stats), token);
        if (done) await SetAsync(db, VersionKey, CurrentVersion, token);
        await db.SaveChangesAsync(token);
    }

    private static async Task SetAsync(AppDbContext db, string key, string value, CancellationToken token)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, token);
        if (setting == null) db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        else setting.Value = value;
    }
}
