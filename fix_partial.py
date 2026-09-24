import sys

# Fix BooksController.cs
path1 = 'QuimeraReader.API/Controllers/BooksController.cs'
with open(path1, 'r', encoding='utf-8') as f:
    content1 = f.read()

content1 = content1.replace('public class BooksController : ControllerBase', 'public partial class BooksController : ControllerBase')
with open(path1, 'w', encoding='utf-8') as f:
    f.write(content1)

# Fix DeleteInternal.cs
path2 = 'QuimeraReader.API/Controllers/BooksController.DeleteInternal.cs'
with open(path2, 'r', encoding='utf-8') as f:
    content2 = f.read()

content2 = content2.replace('using QuimeraReader.Infrastructure.Data;', 'using QuimeraReader.Infrastructure.Persistence;')
with open(path2, 'w', encoding='utf-8') as f:
    f.write(content2)