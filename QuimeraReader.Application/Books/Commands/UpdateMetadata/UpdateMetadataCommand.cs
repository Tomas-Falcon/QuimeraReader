using System.Collections.Generic;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.UpdateMetadata;

public class UpdateMetadataCommand : IRequest<Unit>
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ReadingStatus { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = new List<string>();
}
