using MediatR;
using System.Threading;
using System.Threading.Tasks;
using QuimeraReader.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using System;

namespace QuimeraReader.Application.Books.Commands.UnmatchAudioTrack;

public class UnmatchAudioTrackCommand : IRequest<bool>
{
    public int AudioTrackId { get; set; }
}

public class UnmatchAudioTrackCommandHandler : IRequestHandler<UnmatchAudioTrackCommand, bool>
{
    private readonly IAppDbContext _dbContext;

    public UnmatchAudioTrackCommandHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> Handle(UnmatchAudioTrackCommand request, CancellationToken cancellationToken)
    {
        var track = await _dbContext.BookAudioTracks.FirstOrDefaultAsync(t => t.Id == request.AudioTrackId, cancellationToken);
        if (track == null) return false;

        _dbContext.UnmatchedAudioTracks.Add(new UnmatchedAudioTrack
        {
            OriginalFileName = track.FileName,
            PhysicalPath = track.FilePath,
            FileSizeBytes = 0,
            UploadedAt = DateTime.UtcNow
        });

        _dbContext.BookAudioTracks.Remove(track);
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
