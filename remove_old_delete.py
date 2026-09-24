import sys

path = 'QuimeraReader.API/Controllers/BooksController.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

start_index = content.find('private async Task DeleteBookEntityAndFoldersAsync(Book book)')
if start_index != -1:
    end_index = content.find('public async Task<IActionResult> BulkUpdateStatus', start_index)
    if end_index != -1:
        # find the [HttpPost("bulk/status")] before BulkUpdateStatus
        end_index = content.rfind('[HttpPost("bulk/status")]', start_index, end_index)
        if end_index != -1:
            content = content[:start_index] + content[end_index:]

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)