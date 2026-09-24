import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Fix Dispose
    content = content.replace('NetworkStateService.OnNetworkStateChanged -= HandleNetworkChange;', 'NetworkStateService.OnNetworkStateChanged -= HandleNetworkChange;') # Wait, I should replace it.
    
    # Actually just replace everything
    old_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        _ = NetworkStateService.InitializeAsync();

        NavManager.LocationChanged += (s, e) => { if (_isMobile) _sidebarExpanded = false; StateHasChanged(); };
        if (!ServerConfig.NeedsConfiguration)
        {
            _ = PollScanStatusAsync();
        }
    }'''

    new_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += HandleNetworkChange;
        _ = NetworkStateService.InitializeAsync();

        NavManager.LocationChanged += (s, e) => { if (_isMobile) _sidebarExpanded = false; StateHasChanged(); };
        if (!ServerConfig.NeedsConfiguration)
        {
            _ = PollScanStatusAsync();
        }
        
        var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
        if (worker != null) {
            _ = worker.SyncNowAsync();
        }
    }

    private void HandleNetworkChange()
    {
        InvokeAsync(StateHasChanged);
        if (!NetworkStateService.IsOffline)
        {
            var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
            if (worker != null) {
                _ = worker.SyncNowAsync();
            }
        }
    }'''
    
    content = content.replace(old_init, new_init)
    
    # Fix dispose
    old_dispose = '''    public void Dispose()
    {
        NetworkStateService.OnNetworkStateChanged -= HandleNetworkChange;
        _cts.Cancel();
        _cts.Dispose();
    }'''
    
    content = content.replace(old_dispose, old_dispose) # already correct in dispose

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')