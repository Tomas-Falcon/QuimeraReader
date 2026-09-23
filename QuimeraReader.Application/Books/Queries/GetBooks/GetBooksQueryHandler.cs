using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Books.DTOs;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Books.Queries.GetBooks;

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, PaginatedListDto<BookDto>>
{
    private readonly IAppDbContext _dbContext;

    public GetBooksQueryHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaginatedListDto<BookDto>> Handle(GetBooksQuery request, CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize < 1 ? 50 : (request.PageSize > 100 ? 100 : request.PageSize);

        var query = _dbContext.Books.AsQueryable();

        // Ocultar libros "fantasma" que no tienen archivo asociado (ni EPUB ni Audio)
        query = query.Where(b => !string.IsNullOrEmpty(b.EpubFilePath) || b.AudioTracks.Any());

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(b => 
                b.Title.ToLower().Contains(searchLower) ||
                b.Authors.Any(ba => ba.Author.Name.ToLower().Contains(searchLower)) ||
                b.Categories.Any(bc => bc.Category.Name.ToLower().Contains(searchLower)) ||
                (b.Series != null && b.Series.Name.ToLower().Contains(searchLower))
            );
        }

        var totalBooks = await query.CountAsync(cancellationToken);

        var booksList = await query
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .Include(b => b.Series).ThenInclude(s => s.Universe)
            .OrderByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var books = booksList.Select(b => new BookDto
        {
            Id = b.Id,
            Title = b.Title,
            Isbn = b.Isbn,
            Description = b.Description,
            AverageRating = b.AverageRating,
            ProcessingStatus = b.ProcessingStatus == "SYNCED" ? "ALIGNED" : b.ProcessingStatus,
            Authors = b.Authors.Where(a => a.Author != null).Select(a => a.Author!.Name).ToList(),
            Categories = b.Categories.Where(c => c.Category != null).Select(c => c.Category!.Name).ToList(),
            Series = b.Series?.Name,
            Universe = b.Series?.Universe?.Name,
            HasCover = !string.IsNullOrEmpty(b.CoverImagePath),
            HasEpub = !string.IsNullOrEmpty(b.EpubFilePath),
            HasAudio = b.AudioTracks.Any(),
            IsAligned = b.ProcessingStatus == "SYNCED",
            LastReadAt = b.LastReadAt,
            CurrentEpubCfi = b.CurrentEpubCfi,
            CurrentAudioPosition = b.CurrentAudioPosition,
            PercentageCompleted = b.PercentageCompleted
        }).ToList();

        return new PaginatedListDto<BookDto>
        {
            Total = totalBooks,
            Page = page,
            PageSize = pageSize,
            Data = books
        };
    }
}
