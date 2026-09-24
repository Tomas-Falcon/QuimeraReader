import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Remove the extra code block
    bad_block = '''@code {
    protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        _ = NetworkStateService.InitializeAsync();
    }

    public void Dispose()
    {
        NetworkStateService.OnNetworkStateChanged -= StateHasChanged;
    }
}'''
    content = content.replace(bad_block, '')

    # Insert into the main block
    main_init = '''protected override void OnInitialized()
    {
        NetworkStateService.OnNetworkStateChanged += StateHasChanged;
        _ = NetworkStateService.InitializeAsync();
'''
    content = content.replace('protected override void OnInitialized()\n    {', main_init)

    main_dispose = '''public void Dispose()
    {
        NetworkStateService.OnNetworkStateChanged -= StateHasChanged;'''
    content = content.replace('public void Dispose()\n    {', main_dispose)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor')