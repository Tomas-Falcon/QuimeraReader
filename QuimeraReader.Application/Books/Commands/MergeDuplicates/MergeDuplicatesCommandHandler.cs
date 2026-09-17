using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Books.Commands.MergeDuplicates;

public class MergeDuplicatesCommandHandler : IRequestHandler<MergeDuplicatesCommand, MergeDuplicatesResult>
{
    private readonly IAppDbContext _dbContext;
    private readonly ILogger<MergeDuplicatesCommandHandler> _logger;

    public MergeDuplicatesCommandHandler(IAppDbContext dbContext, ILogger<MergeDuplicatesCommandHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MergeDuplicatesResult> Handle(MergeDuplicatesCommand request, CancellationToken cancellationToken)
    {
        // Traer todos los autores y agruparlos en memoria (porque SQLite no soporta GroupBy en EF Core bien)
        var allAuthors = await _dbContext.Authors.ToListAsync(cancellationToken);
        var duplicateAuthors = allAuthors
            .GroupBy(a => a.Name)
            .Where(g => g.Count() > 1)
            .ToList();
            
        int authorsMerged = 0;
        foreach (var group in duplicateAuthors)
        {
            var ordered = group.OrderBy(a => a.Id).ToList();
            var minId = ordered.First().Id;
            var duplicates = ordered.Skip(1).ToList();
            
            foreach (var dup in duplicates)
            {
                _logger.LogInformation("Fusionando autor duplicado '{Name}' (ID: {DupId}) hacia (ID: {MinId})", dup.Name, dup.Id, minId);
                var bookAuthors = await _dbContext.BookAuthors.Where(ba => ba.AuthorId == dup.Id).ToListAsync(cancellationToken);
                foreach (var ba in bookAuthors)
                {
                    var alreadyExists = await _dbContext.BookAuthors.AnyAsync(x => x.BookId == ba.BookId && x.AuthorId == minId, cancellationToken);
                    if (alreadyExists) {
                        _dbContext.BookAuthors.Remove(ba);
                    } else {
                        _dbContext.BookAuthors.Remove(ba);
                        _dbContext.BookAuthors.Add(new BookAuthor { BookId = ba.BookId, AuthorId = minId, Role = ba.Role });
                    }
                }
                _dbContext.Authors.Remove(dup);
                authorsMerged++;
            }
        }

        // Agrupar y borrar categorías duplicadas en memoria
        var allCategories = await _dbContext.Categories.ToListAsync(cancellationToken);
        var duplicateCategories = allCategories
            .GroupBy(c => c.Name)
            .Where(g => g.Count() > 1)
            .ToList();
            
        int categoriesMerged = 0;
        foreach (var group in duplicateCategories)
        {
            var ordered = group.OrderBy(c => c.Id).ToList();
            var minId = ordered.First().Id;
            var duplicates = ordered.Skip(1).ToList();
            
            foreach (var dup in duplicates)
            {
                _logger.LogInformation("Fusionando categoría duplicada '{Name}' (ID: {DupId}) hacia (ID: {MinId})", dup.Name, dup.Id, minId);
                var bookCategories = await _dbContext.BookCategories.Where(bc => bc.CategoryId == dup.Id).ToListAsync(cancellationToken);
                foreach (var bc in bookCategories)
                {
                    var alreadyExists = await _dbContext.BookCategories.AnyAsync(x => x.BookId == bc.BookId && x.CategoryId == minId, cancellationToken);
                    if (alreadyExists) {
                        _dbContext.BookCategories.Remove(bc);
                    } else {
                        _dbContext.BookCategories.Remove(bc);
                        _dbContext.BookCategories.Add(new BookCategory { BookId = bc.BookId, CategoryId = minId });
                    }
                }
                _dbContext.Categories.Remove(dup);
                categoriesMerged++;
            }
        }
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        // Limpiar huérfanos resultantes
        var orphanedAuthors = await _dbContext.Authors.Include(a => a.Books).Where(a => !a.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedAuthors.Any()) _dbContext.Authors.RemoveRange(orphanedAuthors);

        var orphanedCategories = await _dbContext.Categories.Include(c => c.Books).Where(c => !c.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedCategories.Any()) _dbContext.Categories.RemoveRange(orphanedCategories);

        var orphanedSeries = await _dbContext.Series.Include(s => s.Books).Where(s => !s.Books.Any()).ToListAsync(cancellationToken);
        if (orphanedSeries.Any()) _dbContext.Series.RemoveRange(orphanedSeries);
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        var orphanedUniverses = await _dbContext.Universes.Include(u => u.Series).Where(u => !u.Series.Any()).ToListAsync(cancellationToken);
        if (orphanedUniverses.Any()) _dbContext.Universes.RemoveRange(orphanedUniverses);
        
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Mantenimiento completado. {Authors} autores, {Categories} categorías.", authorsMerged, categoriesMerged);
        
        return new MergeDuplicatesResult
        {
            AuthorsMerged = authorsMerged,
            CategoriesMerged = categoriesMerged,
            Message = $"Mantenimiento completado. Se fusionaron {authorsMerged} autores y {categoriesMerged} categorías duplicadas."
        };
    }
}
