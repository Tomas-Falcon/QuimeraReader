using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>Asigna a un libro la saga leída de su EPUB, reutilizando la saga si ya existe con ese nombre.</summary>
public static class SeriesAssigner
{
    /// <returns>true si el libro ha recibido saga. Nunca pisa una saga ya asignada (p. ej. a mano).</returns>
    public static async Task<bool> ApplyAsync(AppDbContext dbContext, Book book, EpubSeriesInfo info, CancellationToken cancellationToken = default)
    {
        if (book.SeriesId != null || book.Series != null) return false;

        var name = EpubSeriesParser.NormalizeName(info.Name);
        if (name.Length == 0) return false;

        book.Series = await FindOrCreateAsync(dbContext, name, cancellationToken);
        book.SeriesVolume = info.Volume;
        return true;
    }

    public static async Task<Series> FindOrCreateAsync(AppDbContext dbContext, string name, CancellationToken cancellationToken = default)
    {
        // Primero las creadas en este mismo lote (aún sin guardar), luego la base de datos
        // Sin distinguir mayúsculas: "A la caza de un noble" y "A la caza de un Noble" son la misma saga
        var series = dbContext.ChangeTracker.Entries<Series>().Select(e => e.Entity).FirstOrDefault(s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                     ?? await dbContext.Series.FirstOrDefaultAsync(s => EF.Functions.Collate(s.Name, "NOCASE") == name, cancellationToken);
        if (series == null)
        {
            series = new Series { Name = name };
            dbContext.Series.Add(series);
        }
        return series;
    }
}

public static class SeriesMaintenance
{
    /// <summary>
    /// Fusiona sagas que solo se diferencian en mayúsculas (creadas antes de comparar sin distinguirlas):
    /// se queda la más antigua, recibe los libros de las demás y conserva el universo si alguna lo tenía.
    /// </summary>
    /// <returns>Número de sagas eliminadas por fusión.</returns>
    public static async Task<int> MergeCaseDuplicatesAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var all = await dbContext.Series.AsNoTracking().Select(s => new { s.Id, s.Name, s.UniverseId }).ToListAsync(cancellationToken);
        var groups = all.GroupBy(s => s.Name.Trim().ToLowerInvariant()).Where(g => g.Count() > 1).ToList();
        int removed = 0;

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(s => s.Id).ToList();
            var keepId = ordered[0].Id;
            var duplicateIds = ordered.Skip(1).Select(s => s.Id).ToList();

            var keep = await dbContext.Series.FirstAsync(s => s.Id == keepId, cancellationToken);
            keep.UniverseId ??= ordered.Select(s => s.UniverseId).FirstOrDefault(u => u != null);

            var books = await dbContext.Books.Where(b => b.SeriesId != null && duplicateIds.Contains(b.SeriesId.Value)).ToListAsync(cancellationToken);
            foreach (var book in books) book.SeriesId = keepId;

            dbContext.Series.RemoveRange(await dbContext.Series.Where(s => duplicateIds.Contains(s.Id)).ToListAsync(cancellationToken));
            removed += duplicateIds.Count;
        }

        if (removed > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return removed;
    }
}
