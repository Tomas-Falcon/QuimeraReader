import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find the HasCover mapping and insert IsAvailableOffline after it
    # We'll just replace 'HasCover = !string.IsNullOrEmpty(b.CoverImagePath),'
    
    content = content.replace(
        'HasCover = !string.IsNullOrEmpty(book.CoverImagePath),',
        'HasCover = !string.IsNullOrEmpty(book.CoverImagePath),\n            IsAvailableOffline = book.IsAvailableOffline,'
    )
    content = content.replace(
        'HasCover = !string.IsNullOrEmpty(b.CoverImagePath),',
        'HasCover = !string.IsNullOrEmpty(b.CoverImagePath),\n                IsAvailableOffline = b.IsAvailableOffline,'
    )

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')