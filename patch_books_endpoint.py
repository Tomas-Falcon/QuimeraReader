import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    endpoint = '''
    [HttpPut(\"{id}/offline\")]
    public async Task<IActionResult> ToggleOfflineAvailability(int id, [FromQuery] bool isAvailable)
    {
        var book = await _context.Books.FindAsync(id);
        if (book == null) return NotFound();

        book.IsAvailableOffline = isAvailable;
        await _context.SaveChangesAsync();
        return Ok();
    }
'''

    content = content.replace('// --- DELETE (Single/Bulk) ---', endpoint + '\n    // --- DELETE (Single/Bulk) ---')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')