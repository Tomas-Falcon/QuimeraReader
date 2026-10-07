using System.Collections.Generic;
using MediatR;
using QuimeraReader.Application.Books.DTOs;

namespace QuimeraReader.Application.Books.Queries.GetCategories;

public class GetCategoriesQuery : IRequest<List<CategoryDto>>
{
}
