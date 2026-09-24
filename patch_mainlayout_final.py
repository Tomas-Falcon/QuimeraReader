import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Look for OnInitialized
    old_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
        if (worker != null) {
            _ = worker.SyncNowAsync();
        }
    }'''

    if old_init in content:
        new_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += HandleNetworkChange;
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
    else:
        # If the above string wasn't found, find where it just had StateHasChanged
        old2 = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
    }'''
        new2 = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += HandleNetworkChange;
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
        content = content.replace(old2, new2)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')