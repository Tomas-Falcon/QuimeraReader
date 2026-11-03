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
        var series = dbContext.ChangeTracker.Entries<Series>().Select(e => e.Entity).FirstOrDefault(s => s.Name == name)
                     ?? await dbContext.Series.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (series == null)
        {
            series = new Series { Name = name };
            dbContext.Series.Add(series);
        }
        return series;
    }
}
