using System.Collections.Generic;
using MediatR;
using QuimeraReader.Application.Books.DTOs;

namespace QuimeraReader.Application.Books.Queries.GetAuthors;

public class GetAuthorsQuery : IRequest<List<AuthorDto>>
{
    public int Limit { get; set; } = 5000;
    public int Offset { get; set; } = 0;
}
