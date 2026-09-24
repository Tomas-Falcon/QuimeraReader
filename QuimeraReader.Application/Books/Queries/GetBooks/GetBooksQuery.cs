using MediatR;
using QuimeraReader.Application.Books.DTOs;

namespace QuimeraReader.Application.Books.Queries.GetBooks;

public record GetBooksQuery : IRequest<PaginatedListDto<BookDto>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public string? Search { get; init; }
    public string? ReadingStatus { get; init; }
    public System.Collections.Generic.List<int>? CategoryIds { get; init; }
}