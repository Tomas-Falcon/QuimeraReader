using MediatR;
using System.Collections.Generic;

namespace QuimeraReader.Application.Books.Commands.MergeDuplicates;

public record MergeDuplicatesCommand : IRequest<MergeDuplicatesResult>
{
}

public record MergeDuplicatesResult
{
    public int AuthorsMerged { get; init; }
    public int CategoriesMerged { get; init; }
    public string Message { get; init; } = string.Empty;
}
