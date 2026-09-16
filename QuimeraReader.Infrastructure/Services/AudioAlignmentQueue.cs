using System.Threading.Channels;

namespace QuimeraReader.Infrastructure.Services;

public class AudioAlignmentQueue
{
    private readonly Channel<int> _queue;

    public AudioAlignmentQueue()
    {
        // Usamos Bounded channel por si acaso para no volar la RAM si se meten 1 millón de libros
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<int>(options);
    }

    public async ValueTask EnqueueAsync(int bookId, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(bookId, cancellationToken);
    }

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken = default)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }
}
