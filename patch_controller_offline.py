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
'''
    # We will insert it before the last closing brace of the class
    # We'll just search for the last "}" and insert it before
    last_brace_idx = content.rfind('}')
    class_brace_idx = content.rfind('}', 0, last_brace_idx)

    content = content[:class_brace_idx] + new_endpoints + content[class_brace_idx:]

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')