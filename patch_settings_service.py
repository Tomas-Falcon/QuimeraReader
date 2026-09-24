import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    new_service = '''    public async Task<bool> CheckWhisperModelStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/Settings/whisper/status");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
                return result?["isDownloaded"]?.GetValue<bool>() ?? false;
            }
        }
        catch { }
        return false;
    }
'''
    content = content.replace('Task DownloadWhisperModelAsync();', 'Task DownloadWhisperModelAsync();\n    Task<bool> CheckWhisperModelStatusAsync();')
    
    # insert in implementation
    impl_regex = r'(public async Task DownloadWhisperModelAsync\(\).*?response\.EnsureSuccessStatusCode\(\);\s*\})'
    content = re.sub(impl_regex, r'\1\n\n' + new_service, content, flags=re.DOTALL)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Services/SettingsService.cs')