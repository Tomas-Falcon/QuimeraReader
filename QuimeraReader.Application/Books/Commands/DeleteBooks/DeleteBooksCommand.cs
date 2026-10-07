using System.Collections.Generic;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.DeleteBooks;

public class DeleteBooksCommand : IRequest<Unit>
{
    public int[] Ids { get; set; } = Array.Empty<int>();
}
