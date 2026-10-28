using MediatR;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Application.Books.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Books.Queries.GetBooks;

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, PaginatedListDto<BookDto>>
{
    private readonly IAppDbContext _dbContext;

    // Caché corta del COUNT(*) por combinación de filtros: con 138k libros, contar en cada chunk de <Virtualize> era el mayor coste.
    private static readonly ConcurrentDictionary<string, (int Count, DateTime At)> _countCache = new();
    private static readonly TimeSpan CountCacheTtl = TimeSpan.FromSeconds(60);

    public GetBooksQueryHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Invalida la caché de totales (llamar al añadir/borrar libros si se necesita inmediatez).</summary>
    public static void InvalidateCountCache() => _countCache.Clear();

    public async Task<PaginatedListDto<BookDto>> Handle(GetBooksQuery request, CancellationToken cancellationToken)
    {
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize < 1 ? 50 : (request.PageSize > 1000 ? 1000 : request.PageSize);
        int skip = request.Skip ?? ((page - 1) * pageSize);
        int take = request.Take ?? pageSize;
        if (take > 1000) take = 1000;

        var query = _dbContext.Books.AsNoTracking().AsQueryable();

        // Ocultar libros "fantasma" que no tienen archivo asociado (ni EPUB ni Audio)
        query = query.Where(b => (b.EpubFilePath != null && b.EpubFilePath != "") || b.AudioTracks.Any());

        if (!string.IsNullOrWhiteSpace(request.ReadingStatus))
        {
            query = query.Where(b => b.ReadingStatus == request.ReadingStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.AuthorName))
        {
            var authorName = request.AuthorName.Trim();
            query = query.Where(b => b.Authors.Any(a => a.Author != null && a.Author.Name == authorName));
        }

        if (!string.IsNullOrWhiteSpace(request.CategoryName))
        {
            var categoryName = request.CategoryName.Trim();
            query = query.Where(b => b.Categories.Any(c => c.Category != null && c.Category.Name == categoryName));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            var like = $"%{s}%";

            // Resolver primero autores/categorías coincidentes (tablas pequeñas e indexadas) para no escanear 138k libros con LOWER()+EXISTS
            var matchingAuthorIds = await _dbContext.Authors.AsNoTracking()
                .Where(a => EF.Functions.Like(a.Name, like))
                .Select(a => a.Id).Take(500)
                .ToListAsync(cancellationToken);
            var matchingCategoryIds = await _dbContext.Categories.AsNoTracking()
                .Where(c => EF.Functions.Like(c.Name, like))
                .Select(c => c.Id).Take(200)
                .ToListAsync(cancellationToken);

            // Sanitizar caracteres especiales de FTS5 (como ":", "-", "\"", "(", ")") para evitar SqliteException
            var safeFtsTokens = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (safeFtsTokens.Length > 0)
            {
                var ftsSearch = string.Join(" ", safeFtsTokens.Select(t => $"\"{t}\"*"));
                var ftsIds = _dbContext.Books.FromSqlInterpolated($"SELECT b.* FROM Books b INNER JOIN BooksFTS fts ON b.Id = fts.rowid WHERE BooksFTS MATCH {ftsSearch}").Select(f => f.Id);

                query = query.Where(b => ftsIds.Contains(b.Id) ||
                                         b.Authors.Any(a => matchingAuthorIds.Contains(a.AuthorId)) ||
                                         b.Categories.Any(c => matchingCategoryIds.Contains(c.CategoryId)));
            }
            else
            {
                query = query.Where(b => EF.Functions.Like(b.Title, like) ||
                                         b.Authors.Any(a => matchingAuthorIds.Contains(a.AuthorId)) ||
                                         b.Categories.Any(c => matchingCategoryIds.Contains(c.CategoryId)));
            }
        }

        if (request.CategoryIds != null && request.CategoryIds.Any())
        {
            var catIds = request.CategoryIds;
            query = query.Where(b => b.Categories.Any(c => catIds.Contains(c.CategoryId)));
        }

        if (request.IsAvailableOffline == true)
        {
            query = query.Where(b => b.IsAvailableOffline == true);
        }

        // Total (cacheado por firma de filtros)
        var countKey = string.Join("|",
            request.ReadingStatus ?? "", request.AuthorName ?? "", request.CategoryName ?? "", request.Search ?? "",
            request.CategoryIds != null ? string.Join(",", request.CategoryIds.OrderBy(x => x)) : "",
            request.IsAvailableOffline?.ToString() ?? "");
        int totalBooks;
        if (_countCache.TryGetValue(countKey, out var cachedCount) && DateTime.UtcNow - cachedCount.At < CountCacheTtl)
        {
            totalBooks = cachedCount.Count;
        }
        else
        {
            totalBooks = await query.CountAsync(cancellationToken);
            _countCache[countKey] = (totalBooks, DateTime.UtcNow);
            if (_countCache.Count > 500) _countCache.Clear();
        }

        IOrderedQueryable<QuimeraReader.Domain.Entities.Book> ordered;
        switch ((request.SortBy ?? "").Trim().ToLowerInvariant())
        {
            case "title":
                ordered = query.OrderBy(b => b.Title).ThenBy(b => b.Id);
                break;
            case "author":
                ordered = query.OrderBy(b => b.Authors.Select(a => a.Author!.Name).FirstOrDefault()).ThenBy(b => b.Title).ThenBy(b => b.Id);
                break;
            default:
                ordered = !string.IsNullOrWhiteSpace(request.ReadingStatus)
                    ? query.OrderByDescending(b => b.LastReadAt).ThenByDescending(b => b.Id)
                    : query.OrderByDescending(b => b.Id);
                break;
        }

        // Fase 1: solo IDs paginados (consulta ligera)
        var pagedIds = await ordered.Select(b => b.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        if (pagedIds.Count == 0)
        {
            return new PaginatedListDto<BookDto> { Total = totalBooks, Page = page, PageSize = pageSize, Data = new List<BookDto>() };
        }

        // Fase 2: proyección directa en SQL (sin materializar entidades completas ni EpubLocationsCache)
        var rows = await _dbContext.Books.AsNoTracking()
            .Where(b => pagedIds.Contains(b.Id))
            .Select(b => new BookDto
            {
                Id = b.Id,
                Title = b.Title,
                Isbn = b.Isbn,
                Description = b.Description,
                AverageRating = b.AverageRating,
                ProcessingStatus = b.ProcessingStatus == "SYNCED" ? "ALIGNED" : b.ProcessingStatus,
                Authors = b.Authors.Where(a => a.Author != null).Select(a => a.Author!.Name).ToList(),
                Categories = b.Categories.Where(c => c.Category != null).Select(c => c.Category!.Name).ToList(),
                Series = b.Series != null ? b.Series.Name : null,
                Universe = b.Series != null && b.Series.Universe != null ? b.Series.Universe.Name : null,
                IsAvailableOffline = b.IsAvailableOffline,
                HasCover = b.CoverImagePath != null && b.CoverImagePath != "",
                HasEpub = b.EpubFilePath != null && b.EpubFilePath != "",
                HasAudio = b.AudioTracks.Any(),
                IsAligned = b.ProcessingStatus == "SYNCED",
                LastReadAt = b.LastReadAt,
                CurrentEpubCfi = b.CurrentEpubCfi,
                CurrentAudioPosition = b.CurrentAudioPosition,
                CurrentAudioTrackNumber = b.CurrentAudioTrackNumber,
                PercentageCompleted = b.PercentageCompleted,
                ReadingStatus = b.ReadingStatus,
                EpubLocationsCache = null,
                TotalPages = b.TotalPages
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // Restaurar el orden de la fase 1
        var position = pagedIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
        var books = rows.OrderBy(r => position[r.Id]).ToList();

        return new PaginatedListDto<BookDto>
        {
            Total = totalBooks,
            Page = page,
            PageSize = pageSize,
            Data = books
        };
    }
}
