import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/QuimeraReader.Shared.csproj'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<Folder Include=\"wwwroot\Translations\\\" />', '<EmbeddedResource Include=\"wwwroot\Translations\**\*.json\" />')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)