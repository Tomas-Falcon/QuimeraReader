import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    new_endpoints = '''
    [HttpPut("{id}/offline")]
    public async Task<IActionResult> ToggleOfflineAvailability(int id, [FromQuery] bool isAvailable)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        book.IsAvailableOffline = isAvailable;
        await _dbContext.SaveChangesAsync();

        return Ok(new { book.Id, book.IsAvailableOffline });
    }

    [HttpPost("{id}/annotations")]
    public async Task<IActionResult> CreateAnnotation(int id, [FromBody] QuimeraReader.Domain.Entities.BookAnnotation annotation)
    {
        var book = await _dbContext.Books.FindAsync(id);
        if (book == null) return NotFound();

        annotation.BookId = id;
        annotation.CreatedAt = DateTime.UtcNow;
        
        _dbContext.BookAnnotations.Add(annotation);
        await _dbContext.SaveChangesAsync();

        return Ok(annotation);
    }
}'''

    # Replace the last closing brace of the BooksController class
    last_idx = content.rfind('}')
    second_last = content.rfind('}', 0, last_idx - 1)
    
    # We should search for public class ScanRequest because it's at the end
    if 'public class ScanRequest' in content:
        content = content.replace('public class ScanRequest', new_endpoints + '\npublic class ScanRequest')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')