using System.IO;
using MediatR;

namespace QuimeraReader.Application.Books.Commands.UploadBook;

public class UploadBookCommand : IRequest<UploadBookResultDto>
{
    public Stream FileStream { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public long Length { get; set; }
}

public class UploadBookResultDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool HasEpub { get; set; }
    public bool HasCover { get; set; }
    public bool IsAvailableOffline { get; set; }
    public bool HasAudio { get; set; }
    public string Message { get; set; } = string.Empty;
}
