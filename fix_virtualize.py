import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<div class=\"library-container\">', '<div class=\"library-container\" style=\"height: calc(100vh - 120px); overflow-y: auto; overflow-x: hidden;\">')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)