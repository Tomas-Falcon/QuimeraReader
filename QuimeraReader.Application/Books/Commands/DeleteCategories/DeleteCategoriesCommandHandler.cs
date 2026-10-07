using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;

namespace QuimeraReader.Application.Books.Commands.DeleteCategories;

public class DeleteCategoriesCommandHandler : IRequestHandler<DeleteCategoriesCommand, Unit>
{
    private readonly IAppDbContext _dbContext;

    public DeleteCategoriesCommandHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Unit> Handle(DeleteCategoriesCommand request, CancellationToken cancellationToken)
    {
        if (request.Ids == null || request.Ids.Length == 0) return Unit.Value;

        var categories = await _dbContext.Categories.Where(c => request.Ids.Contains(c.Id)).ToListAsync(cancellationToken);
        if (!categories.Any()) return Unit.Value;

        _dbContext.Categories.RemoveRange(categories);
        await _dbContext.SaveChangesAsync(cancellationToken);
        
        return Unit.Value;
    }
}
