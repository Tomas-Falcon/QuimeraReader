using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Books.DTOs;

namespace QuimeraReader.Application.Books.Queries.GetCategories;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, List<CategoryDto>>
{
    private readonly IAppDbContext _dbContext;

    public GetCategoriesQueryHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _dbContext.Categories
            .Select(c => new CategoryDto {
                Id = c.Id,
                Name = c.Name,
                IsUserGenerated = c.IsUserGenerated,
                BookCount = c.Books.Count,
                SampleCoverUrl = c.Books.Where(b => b.Book.CoverImagePath != null)
                                        .Select(b => "api/media/books/" + b.Book.Id + "/cover")
                                        .FirstOrDefault()
            })
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return categories;
    }
}
