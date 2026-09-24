import sys

path = 'QuimeraReader.Infrastructure/Services/EpubScannerService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

injection = '''
        if (!book.IsMetadataComplete)
        {
            var faltaMetaCat = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == "Falta Metadatos");
            if (faltaMetaCat == null)
            {
                faltaMetaCat = new Category { Name = "Falta Metadatos", IsUserGenerated = false };
                _dbContext.Add(faltaMetaCat);
            }
            if (!book.Categories.Any(c => c.Category.Name == "Falta Metadatos"))
            {
                book.Categories.Add(new BookCategory { Category = faltaMetaCat });
            }
        }
        else
        {
            var faltaMetaCat = book.Categories.FirstOrDefault(c => c.Category.Name == "Falta Metadatos");
            if (faltaMetaCat != null)
            {
                book.Categories.Remove(faltaMetaCat);
            }
        }
'''

content = content.replace('book.IsMetadataComplete = hasTitle && hasAuthor && hasSynopsis && hasCover && ratingSatisfied;', 'book.IsMetadataComplete = hasTitle && hasAuthor && hasSynopsis && hasCover && ratingSatisfied;' + injection)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)