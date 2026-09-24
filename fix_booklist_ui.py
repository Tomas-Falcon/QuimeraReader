import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    '<div class=\"filters d-flex flex-wrap gap-2 align-items-center w-100\">',
    '<div class=\"filters d-flex flex-column flex-sm-row flex-wrap gap-2 align-items-stretch align-items-sm-center w-100\">'
)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)