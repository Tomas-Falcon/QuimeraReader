using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Interfaces;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Recalcula la puntuación de los libros ya importados con <see cref="RatingLookup"/>: se queda la de más votos.
/// Solo toca libros sin puntuación fiable (sin votos guardados o con menos de los mínimos), salvo con all=true.
/// </summary>
public class RatingRefreshService
{
    // Hardcover limita las peticiones por minuto
    private static readonly TimeSpan DelayBetweenBooks = TimeSpan.FromMilliseconds(1100);

    private readonly AppDbContext _db;
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<RatingRefreshService> _logger;

    public RatingRefreshService(AppDbContext db, IEnumerable<IMetadataProvider> providers, ILogger<RatingRefreshService> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
    }

    public class Report
    {
        public int Checked { get; set; }
        public int Updated { get; set; }
        public int NotFound { get; set; }
        public int Errors { get; set; }
        public bool Done { get; set; }
    }

    public async Task<Report> RunAsync(bool all, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var report = new Report();
        var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        int lastId = 0;
        while (true)
        {
            var query = _db.Books.Include(b => b.Authors).ThenInclude(ba => ba.Author).Where(b => b.Id > lastId);
            if (!all) query = query.Where(b => b.RatingsCount == null || b.RatingsCount < RatingPolicy.MinVotes);
            var batch = await query.OrderBy(b => b.Id).Take(200).ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            foreach (var book in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.Checked++;
                try
                {
                    var author = book.Authors.FirstOrDefault()?.Author?.Name;
                    var best = await RatingLookup.FindBestAsync(_providers, book.Title, author, settings);
                    if (best == null) report.NotFound++;
                    else if (RatingLookup.Apply(book, best))
                    {
                        report.Updated++;
                        _logger.LogInformation("Puntuación de '{Title}': {Rating} ({Votes} votos, {Source})", book.Title, best.Value.Value, best.Value.Votes, best.Value.Source);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    report.Errors++;
                    _logger.LogWarning(ex, "No se pudo actualizar la puntuación de '{Title}'", book.Title);
                }
                onProgress?.Invoke(report.Checked);
                await Task.Delay(DelayBetweenBooks, cancellationToken);
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
        report.Done = true;
        return report;
    }
}
