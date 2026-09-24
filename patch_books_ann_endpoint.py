import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    endpoint = '''
    public class CreateAnnotationRequest
    {
        public string CfiRange { get; set; } = string.Empty;
        public string SelectedText { get; set; } = string.Empty;
        public string? ColorHex { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost(\"{id}/annotations\")]
    public async Task<IActionResult> CreateAnnotation(int id, [FromBody] CreateAnnotationRequest req)
    {
        var book = await _context.Books.FindAsync(id);
        if (book == null) return NotFound();

        var ann = new QuimeraReader.Domain.Entities.BookAnnotation
        {
            BookId = id,
            CfiRange = req.CfiRange,
            SelectedText = req.SelectedText,
            ColorHex = req.ColorHex,
            Note = req.Note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        _context.BookAnnotations.Add(ann);
        await _context.SaveChangesAsync();
        
        return Ok(new { ann.Id });
    }
'''

    content = content.replace('// --- DELETE (Single/Bulk) ---', endpoint + '\n    // --- DELETE (Single/Bulk) ---')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')