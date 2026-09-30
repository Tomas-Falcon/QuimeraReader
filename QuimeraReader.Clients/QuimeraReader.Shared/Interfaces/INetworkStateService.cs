using System;
using System.Threading.Tasks;

namespace QuimeraReader.Shared.Interfaces
{
    public interface INetworkStateService
    {
        bool IsOffline { get; }
        bool IsForceOffline { get; }
        event Action OnNetworkStateChanged;
        Task InitializeAsync();
        void SetForceOffline(bool forceOffline);
    }
}