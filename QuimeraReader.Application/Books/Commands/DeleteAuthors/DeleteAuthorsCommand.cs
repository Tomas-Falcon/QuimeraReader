using System.Collections.Generic;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.DeleteAuthors;

public class DeleteAuthorsCommand : IRequest<Unit>
{
    public int[] Ids { get; set; } = Array.Empty<int>();
}
