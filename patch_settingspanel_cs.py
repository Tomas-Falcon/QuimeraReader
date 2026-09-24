import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Add boolean property
    content = content.replace('private bool _isDarkMode;', 'private bool _isDarkMode;\n    private bool _isForceOffline;')
    
    # Initialize it
    content = content.replace('_isDarkMode = true;', '_isDarkMode = true;\n        _isForceOffline = NetworkStateService.IsForceOffline;')
    
    # Add OnForceOfflineChanged
    method = '''
    private void OnForceOfflineChanged(bool value)
    {
        NetworkStateService.SetForceOffline(value);
        ToastService.ShowSuccess(value ? ""Modo sin conexión forzado"" : ""Modo en línea restaurado"");
    }
'''
    content = content.replace('private async Task LoadSettingsAsync()', method + '\n    private async Task LoadSettingsAsync()')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor.cs')