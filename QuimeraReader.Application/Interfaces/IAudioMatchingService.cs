using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Interfaces;

public interface IAudioMatchingService
{
    Task<int?> TryMatchAudioToBookAsync(string audioFilePath, CancellationToken cancellationToken = default);
}
