using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Books.Commands.UpdateMetadata;

public class UpdateMetadataCommandHandler : IRequestHandler<UpdateMetadataCommand, Unit>
{
    private readonly IAppDbContext _dbContext;

    public UpdateMetadataCommandHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Unit> Handle(UpdateMetadataCommand request, CancellationToken cancellationToken)
    {
        var book = await _dbContext.Books
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .FirstOrDefaultAsync(b => b.Id == request.BookId, cancellationToken);
            
        if (book == null) return Unit.Value; // Optionally throw NotFoundException

        if (book.ReadingStatus != request.ReadingStatus && 
           (request.ReadingStatus == "Reading" || request.ReadingStatus == "NextToRead" || request.ReadingStatus == "Read"))
        {
            book.LastReadAt = DateTime.UtcNow;
        }
        
        book.Title = request.Title;
        book.ReadingStatus = request.ReadingStatus;
        
        // Remove old categories not in new list
        var toRemove = book.Categories.Where(c => !request.Categories.Contains(c.Category.Name)).ToList();
        foreach (var r in toRemove) book.Categories.Remove(r);

        // Add new categories
        var existingNames = book.Categories.Select(c => c.Category.Name).ToList();
        var toAdd = request.Categories.Where(c => !existingNames.Contains(c)).ToList();
        
        foreach (var newCatName in toAdd)
        {
            var cat = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == newCatName, cancellationToken);
            if (cat == null) 
            {
                cat = new Category { Name = newCatName };
                _dbContext.Categories.Add(cat);
                await _dbContext.SaveChangesAsync(cancellationToken); 
            }
            book.Categories.Add(new BookCategory { BookId = book.Id, CategoryId = cat.Id });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
