using System.IO;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.ScanFolder;

public class ScanFolderCommand : IRequest<Unit>
{
    public string FolderPath { get; set; } = string.Empty;
}
