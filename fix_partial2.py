import sys

# Fix DeleteInternal.cs
path2 = 'QuimeraReader.API/Controllers/BooksController.DeleteInternal.cs'
with open(path2, 'r', encoding='utf-8') as f:
    content2 = f.read()

content2 = content2.replace('using QuimeraReader.Infrastructure.Persistence;', 'using QuimeraReader.Infrastructure;')

with open(path2, 'w', encoding='utf-8') as f:
    f.write(content2)