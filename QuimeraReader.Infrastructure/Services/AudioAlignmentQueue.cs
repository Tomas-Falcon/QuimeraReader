using System.Collections.Concurrent;
using System.Threading.Channels;

namespace QuimeraReader.Infrastructure.Services;

public class AudioAlignmentQueue
{
    private readonly Channel<int> _queue;
    private readonly ConcurrentDictionary<int, byte> _queuedBookIds = new();

    public AudioAlignmentQueue()
    {
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };
        _queue = Channel.CreateBounded<int>(options);
    }

    public async ValueTask EnqueueAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (bookId <= 0) return;
        if (_queuedBookIds.TryAdd(bookId, 0))
        {
            await _queue.Writer.WriteAsync(bookId, cancellationToken);
        }
    }

    public async IAsyncEnumerable<int> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var bookId in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            _queuedBookIds.TryRemove(bookId, out _);
            yield return bookId;
        }
    }
}
