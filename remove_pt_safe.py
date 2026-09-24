import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/MainLayout.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('class=\"pt-safe\" style=\"border-bottom: 1px solid var(--border-color)', 'style=\"border-bottom: 1px solid var(--border-color)')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)