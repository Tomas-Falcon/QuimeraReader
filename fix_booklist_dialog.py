import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('@inject IBookService BookService', '@inject IBookService BookService\n@inject Radzen.DialogService DialogService')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)