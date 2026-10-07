using MediatR;
using System.Threading;
using System.Threading.Tasks;
using QuimeraReader.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace QuimeraReader.Application.Books.Commands.ReassignAudioTrack;

public class ReassignAudioTrackCommand : IRequest<bool>
{
    public int AudioTrackId { get; set; }
    public int NewBookId { get; set; }
}

public class ReassignAudioTrackCommandHandler : IRequestHandler<ReassignAudioTrackCommand, bool>
{
    private readonly IAppDbContext _dbContext;

    public ReassignAudioTrackCommandHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> Handle(ReassignAudioTrackCommand request, CancellationToken cancellationToken)
    {
        var track = await _dbContext.BookAudioTracks.FirstOrDefaultAsync(t => t.Id == request.AudioTrackId, cancellationToken);
        if (track == null) return false;

        var newBook = await _dbContext.Books.Include(b => b.AudioTracks).FirstOrDefaultAsync(b => b.Id == request.NewBookId, cancellationToken);
        if (newBook == null) return false;

        track.BookId = request.NewBookId;
        track.TrackNumber = newBook.AudioTracks.Any() ? newBook.AudioTracks.Max(t => t.TrackNumber) + 1 : 1;
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
