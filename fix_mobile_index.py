import sys

path = 'QuimeraReader.Clients/QuimeraReader.Mobile/wwwroot/index.html'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('app.css?v=2', 'app.css?v=3')
content = content.replace('dark-base.css', 'dark.css')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)