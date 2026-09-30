using System;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using QuimeraReader.Shared.Interfaces;
using Microsoft.Maui.Storage;

namespace QuimeraReader.Mobile.Services
{
    public class MauiNetworkStateService : INetworkStateService
    {
        public bool IsOffline => IsForceOffline || Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
        public bool IsForceOffline { get; private set; }

        public event Action? OnNetworkStateChanged;

        public MauiNetworkStateService()
        {
            Connectivity.Current.ConnectivityChanged += (s, e) => OnNetworkStateChanged?.Invoke();
        }

        public Task InitializeAsync()
        {
            IsForceOffline = Preferences.Default.Get("ForceOffline", false);
            return Task.CompletedTask;
        }

        public void SetForceOffline(bool forceOffline)
        {
            IsForceOffline = forceOffline;
            Preferences.Default.Set("ForceOffline", forceOffline);
            OnNetworkStateChanged?.Invoke();
        }
    }
}