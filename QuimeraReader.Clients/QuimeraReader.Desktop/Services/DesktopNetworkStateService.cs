using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using QuimeraReader.Shared.Interfaces;

namespace QuimeraReader.Desktop.Services;

public class DesktopNetworkStateService : INetworkStateService
{
    private const string ForceOfflineKey = "ForceOffline";

    public bool IsOffline => IsForceOffline || !NetworkInterface.GetIsNetworkAvailable();
    public bool IsForceOffline { get; private set; }

    public event Action? OnNetworkStateChanged;

    public DesktopNetworkStateService()
    {
        NetworkChange.NetworkAddressChanged += (s, e) => OnNetworkStateChanged?.Invoke();
        NetworkChange.NetworkAvailabilityChanged += (s, e) => OnNetworkStateChanged?.Invoke();
    }

    public Task InitializeAsync()
    {
        IsForceOffline = DesktopPreferences.Get(ForceOfflineKey, false);
        return Task.CompletedTask;
    }

    public void SetForceOffline(bool forceOffline)
    {
        IsForceOffline = forceOffline;
        DesktopPreferences.Set(ForceOfflineKey, forceOffline);
        OnNetworkStateChanged?.Invoke();
    }
}
