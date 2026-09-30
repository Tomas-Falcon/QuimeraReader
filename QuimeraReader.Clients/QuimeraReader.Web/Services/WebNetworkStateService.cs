using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using QuimeraReader.Shared.Interfaces;

namespace QuimeraReader.Web.Services
{
    public class WebNetworkStateService : INetworkStateService
    {
        private readonly IJSRuntime _jsRuntime;
        public bool IsOffline { get; private set; }
        public bool IsForceOffline { get; private set; }

        public event Action? OnNetworkStateChanged;

        public WebNetworkStateService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            // Web implementation simplified for now
            IsOffline = false;
        }

        public void SetForceOffline(bool forceOffline)
        {
            IsForceOffline = forceOffline;
            OnNetworkStateChanged?.Invoke();
        }
    }
}