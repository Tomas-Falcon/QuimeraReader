using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using QuimeraReader.Infrastructure;
using QuimeraReader.Infrastructure.Services;

namespace QuimeraReader.API.BackgroundServices;

/// <summary>
/// Calcula una sola vez el hash de los EPUB ya importados (antes no se guardaba), para que el escáner reconozca
/// las copias repetidas y la limpieza pueda unir duplicados. Por lotes, reanudable y con pausas para no acaparar el disco.
/// </summary>
public class ContentHashBackfillBackgroundService : BackgroundService
{
    public const string VersionKey = "ContentHashBackfillVersion";
    public const string LastIdKey = "ContentHashBackfillLastId";
    public const string StatsKey = "ContentHashBackfillStats";
    private const string CurrentVersion = "1";
    private const int BatchSize = 200;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentHashBackfillBackgroundService> _logger;

    public ContentHashBackfillBackgroundService(IServiceProvider serviceProvider, ILogger<ContentHashBackfillBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public class Stats
    {
        public int Scanned { get; set; }
        public int Hashed { get; set; }
        public int MissingFile { get; set; }
        public int Errors { get; set; }
        public bool Done { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken); } catch (TaskCanceledException) { return; }

        try
        {
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculando hashes de EPUB; se reintentará en el próximo arranque.");
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

        _logger.LogInformation("Hashes de EPUB: empezando desde el libro {LastId}", lastId);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var batch = await db.Books
                .Where(b => b.Id > lastId && b.ContentHash == null && b.EpubFilePath != null && b.EpubFilePath != "")
                .OrderBy(b => b.Id)
                .Take(BatchSize)
                .ToListAsync(stoppingToken);

            if (batch.Count == 0)
            {
                stats.Done = true;
                await SaveProgressAsync(db, lastId, stats, done: true, stoppingToken);
                _logger.LogInformation("Hashes de EPUB terminados: {Hashed} calculados, {Missing} sin archivo, {Errors} errores",
                    stats.Hashed, stats.MissingFile, stats.Errors);
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
                    book.ContentHash = await BookFingerprint.ComputeAsync(path, stoppingToken);
                    stats.Hashed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stats.Errors++;
                    _logger.LogDebug(ex, "No se pudo calcular el hash de {Path}", path);
                }
            }

            lastId = batch[^1].Id;
            await db.SaveChangesAsync(stoppingToken);
            await SaveProgressAsync(db, lastId, stats, done: false, stoppingToken);

            if (stats.Scanned % 5000 < BatchSize)
            {
                _logger.LogInformation("Hashes de EPUB: {Scanned} libros revisados (último id {LastId})", stats.Scanned, lastId);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
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
