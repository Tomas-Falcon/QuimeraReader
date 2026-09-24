using System.Threading.Tasks;

namespace QuimeraReader.Shared.Interfaces;

public interface IOfflineSyncWorker
{
    Task SyncNowAsync();
    bool IsSyncing { get; }
}
