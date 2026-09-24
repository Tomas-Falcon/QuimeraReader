import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Add using Microsoft.Extensions.DependencyInjection
    if 'using Microsoft.Extensions.DependencyInjection;' not in content:
        content = content.replace('@using Microsoft.JSInterop', '@using Microsoft.JSInterop\n@using Microsoft.Extensions.DependencyInjection')

    # Add Provider injection
    if '@inject IServiceProvider ServiceProvider' not in content:
        content = content.replace('@inject IJSRuntime JSRuntime', '@inject IJSRuntime JSRuntime\n@inject IServiceProvider ServiceProvider')

    # Add SyncNowAsync trigger
    old_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
    }'''

    new_init = '''    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        var worker = ServiceProvider.GetService<QuimeraReader.Shared.Interfaces.IOfflineSyncWorker>();
        if (worker != null) {
            _ = worker.SyncNowAsync();
        }
    }'''
    content = content.replace(old_init, new_init)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')