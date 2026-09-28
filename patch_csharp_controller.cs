using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Fixer
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = @""QuimeraReader.API/Controllers/BooksController.cs"";
            string content = File.ReadAllText(path);

            string newEndpoints = @""

        [HttpPut(""""{id}/offline"""")]
        public async Task<IActionResult> ToggleOfflineAvailability(int id, [FromQuery] bool isAvailable)
        {
            var book = await _dbContext.Books.FindAsync(id);
            if (book == null) return NotFound();

            book.IsAvailableOffline = isAvailable;
            await _dbContext.SaveChangesAsync();

            return Ok(new { book.Id, book.IsAvailableOffline });
        }

        [HttpPost(""""{id}/annotations"""")]
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
    }
}
"";
            // Replace the last "} }" with our new endpoints and close the class/namespace
            int lastIndex = content.LastIndexOf('}');
            int secondLastIndex = content.LastIndexOf('}', lastIndex - 1);
            
            content = content.Substring(0, secondLastIndex) + newEndpoints;

            File.WriteAllText(path, content);
        }
    }
}