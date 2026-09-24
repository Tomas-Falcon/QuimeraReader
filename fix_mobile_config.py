import sys

path = 'QuimeraReader.Clients/QuimeraReader.Mobile/Services/MobileServerConfigService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Get<string>(ServerUrlKey, null)', 'Get(ServerUrlKey, \"\")')
content = content.replace('public string? ServerUrl => Preferences.Default.Get(ServerUrlKey, \"\");', 'public string? ServerUrl { get { var url = Preferences.Default.Get(ServerUrlKey, \"\"); return string.IsNullOrWhiteSpace(url) ? null : url; } }')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)