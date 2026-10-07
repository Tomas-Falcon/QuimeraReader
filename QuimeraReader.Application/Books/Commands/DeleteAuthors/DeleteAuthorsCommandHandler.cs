using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Books.Commands.DeleteBooks;

namespace QuimeraReader.Application.Books.Commands.DeleteAuthors;

public class DeleteAuthorsCommandHandler : IRequestHandler<DeleteAuthorsCommand, Unit>
{
    private readonly IAppDbContext _dbContext;
    private readonly IMediator _mediator;

    public DeleteAuthorsCommandHandler(IAppDbContext dbContext, IMediator mediator)
    {
        _dbContext = dbContext;
        _mediator = mediator;
    }

    public async Task<Unit> Handle(DeleteAuthorsCommand request, CancellationToken cancellationToken)
    {
        if (request.Ids == null || request.Ids.Length == 0) return Unit.Value;

        var authors = await _dbContext.Authors.Where(a => request.Ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (!authors.Any()) return Unit.Value;

        var books = await _dbContext.Books
            .Where(b => b.Authors.Any(a => request.Ids.Contains(a.AuthorId)))
            .Select(b => b.Id)
            .ToArrayAsync(cancellationToken);

        if (books.Any())
        {
            await _mediator.Send(new DeleteBooksCommand { Ids = books }, cancellationToken);
        }

        // Clean up empty authors
        var remainingAuthors = await _dbContext.Authors.Where(a => request.Ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (remainingAuthors.Any())
        {
            _dbContext.Authors.RemoveRange(remainingAuthors);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
