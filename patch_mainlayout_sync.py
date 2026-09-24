import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
        if (worker != null) {
            _ = worker.SyncNowAsync();
        }
    }'''

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
        StateHasChanged();
        if (!NetworkStateService.IsOffline)
        {
            var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
            if (worker != null) {
                _ = worker.SyncNowAsync();
            }
        }
    }'''
    content = content.replace(old_init, new_init)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')