import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # add _isWhisperDownloaded bool
    content = content.replace('private bool _isDownloadingWhisper = false;', 'private bool _isDownloadingWhisper = false;\n    private bool _isWhisperDownloaded = false;')

    # in OnInitializedAsync add the check
    old_init = '''    protected override async Task OnInitializedAsync()
    {
        await LoadSettingsAsync();
    }'''
    new_init = '''    protected override async Task OnInitializedAsync()
    {
        await LoadSettingsAsync();
        _isWhisperDownloaded = await SettingsService.CheckWhisperModelStatusAsync();
    }'''
    content = content.replace(old_init, new_init)

    # in DownloadWhisperModelAsync update flag
    old_dl = '''            await SettingsService.DownloadWhisperModelAsync();
            ShowMessage("Modelo descargado con éxito.", true);'''
    new_dl = '''            await SettingsService.DownloadWhisperModelAsync();
            _isWhisperDownloaded = true;
            ShowMessage("Modelo descargado y sincronización reanudada.", true);'''
    content = content.replace(old_dl, new_dl)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor.cs')