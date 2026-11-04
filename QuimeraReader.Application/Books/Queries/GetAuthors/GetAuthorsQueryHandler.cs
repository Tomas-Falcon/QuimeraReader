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
            var raw = await _dbContext.Authors
                .AsNoTracking()
                .Where(a => a.Name != null && a.Books.Any())
                .OrderBy(a => a.Name)
                .Skip(request.Offset)
                .Take(request.Limit)
                .Select(a => new {
                    a.Id,
                    a.Name,
                    BookCount = a.Books.Count(),
                    SampleBookId = a.Books.Select(ba => (int?)ba.BookId).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var authors = raw.Select(a => new AuthorDto {
                Id = a.Id,
                Name = a.Name ?? "Desconocido",
                ProfileImageUrl = null,
                BookCount = a.BookCount,
                SampleCoverUrl = a.SampleBookId.HasValue ? $"api/media/books/{a.SampleBookId.Value}/cover" : null
            }).ToList();

            return authors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar autores en GetAuthorsQueryHandler");
            return new List<AuthorDto>();
        }
    }
}
