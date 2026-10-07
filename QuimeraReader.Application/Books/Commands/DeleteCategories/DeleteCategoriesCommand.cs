using System.Collections.Generic;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.DeleteCategories;

public class DeleteCategoriesCommand : IRequest<Unit>
{
    public int[] Ids { get; set; } = Array.Empty<int>();
}
