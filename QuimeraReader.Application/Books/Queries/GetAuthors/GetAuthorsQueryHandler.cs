using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Books.DTOs;

namespace QuimeraReader.Application.Books.Queries.GetAuthors;

public class GetAuthorsQueryHandler : IRequestHandler<GetAuthorsQuery, List<AuthorDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly ILogger<GetAuthorsQueryHandler> _logger;

    public GetAuthorsQueryHandler(IAppDbContext dbContext, ILogger<GetAuthorsQueryHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<AuthorDto>> Handle(GetAuthorsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var query = _dbContext.Authors
                .Where(a => a.Name != null)
                .GroupBy(a => a.Name)
                .Select(g => g.OrderBy(a => a.Id).FirstOrDefault());

            var authors = await query
                .Select(a => new AuthorDto {
                    Id = a!.Id,
                    Name = a.Name ?? "Desconocido",
                    ProfileImageUrl = null,
                    BookCount = a.Books.Count,
                    SampleCoverUrl = a.Books.Where(b => b.Book.CoverImagePath != null)
                                            .Select(b => "api/media/books/" + b.Book.Id + "/cover")
                                            .FirstOrDefault()
                })
                .OrderBy(a => a.Name)
                .Skip(request.Offset)
                .Take(request.Limit)
                .ToListAsync(cancellationToken);

            return authors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar autores en GetAuthorsQueryHandler");
            return new List<AuthorDto>();
        }
    }
}
