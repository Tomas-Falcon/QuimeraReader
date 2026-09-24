import sys

path = 'QuimeraReader.Clients/QuimeraReader.Mobile/QuimeraReader.Mobile.csproj'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<ApplicationTitle>QuimeraReader.Mobile</ApplicationTitle>', '<ApplicationTitle>QuimeraReader</ApplicationTitle>')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)