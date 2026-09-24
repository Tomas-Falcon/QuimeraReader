import sys

path = 'QuimeraReader.API/Controllers/BooksController.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Modify DeleteAuthors
old_delete_authors = '''        foreach (var author in authors)
        {
            var books = await _dbContext.Books.Where(b => b.Authors.Any(a => a.AuthorId == author.Id)).ToListAsync();
            foreach (var book in books)
            {
                if (!string.IsNullOrEmpty(book.SourceFilePath) && System.IO.File.Exists(book.SourceFilePath))
                {
                    try { System.IO.File.Delete(book.SourceFilePath); } catch { }
                }
                if (!string.IsNullOrEmpty(book.EpubFilePath) && System.IO.File.Exists(book.EpubFilePath))
                {
                    try { System.IO.File.Delete(book.EpubFilePath); } catch { }
                }
                if (!string.IsNullOrEmpty(book.CoverImagePath) && System.IO.File.Exists(book.CoverImagePath))
                {
                    try { System.IO.File.Delete(book.CoverImagePath); } catch { }
                }
                
                var audioTracks = await _dbContext.BookAudioTracks.Where(a => a.BookId == book.Id).ToListAsync();
                foreach(var track in audioTracks)
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && System.IO.File.Exists(track.FilePath))
                    {
                        try { System.IO.File.Delete(track.FilePath); } catch { }
                    }
                }
            }
            _dbContext.Books.RemoveRange(books);
        }

        _dbContext.Authors.RemoveRange(authors);
        await _dbContext.SaveChangesAsync();
        return Ok();'''

new_delete_authors = '''        foreach (var author in authors)
        {
            var books = await _dbContext.Books.Include(b => b.AudioTracks).Where(b => b.Authors.Any(a => a.AuthorId == author.Id)).ToListAsync();
            if (books.Any()) {
                await DeleteBooksInternalAsync(books);
            }
        }

        // Si quedaron autores vacios (por ej, subidos a mano) los borramos
        var remainingAuthors = await _dbContext.Authors.Where(a => ids.Contains(a.Id)).ToListAsync();
        if (remainingAuthors.Any())
        {
            _dbContext.Authors.RemoveRange(remainingAuthors);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();'''

content = content.replace(old_delete_authors, new_delete_authors)

# Add [HttpDelete] para multiples libros
add_delete_books = '''    [HttpDelete]
    public async Task<IActionResult> DeleteBooks([FromQuery] int[] ids)
    {
        if (ids == null || ids.Length == 0) return BadRequest();
        var books = await _dbContext.Books.Include(b => b.AudioTracks).Where(b => ids.Contains(b.Id)).ToListAsync();
        if (books.Any()) {
            await DeleteBooksInternalAsync(books);
        }
        return Ok();
    }
'''
content = content.replace('[HttpDelete("authors")]', add_delete_books + '\n    [HttpDelete("authors")]')

# DeleteBookEntityAndFoldersAsync should call the new one
content = content.replace('await DeleteBookEntityAndFoldersAsync(book);', 'await DeleteBooksInternalAsync(new List<Book> { book });')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)