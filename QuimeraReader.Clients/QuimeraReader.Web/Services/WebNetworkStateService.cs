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
            var isOnline = await _jsRuntime.InvokeAsync<bool>("window.quimeraNetwork.isOnline");
            IsOffline = !isOnline;
            
            var dotNetRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("window.quimeraNetwork.registerListener", dotNetRef);
        }

        [JSInvokable]
        public void OnNetworkStateChangedJS(bool isOnline)
        {
            IsOffline = !isOnline;
            OnNetworkStateChanged?.Invoke();
        }

        public void SetForceOffline(bool forceOffline)
        {
            IsForceOffline = forceOffline;
            OnNetworkStateChanged?.Invoke();
        }
    }
}
