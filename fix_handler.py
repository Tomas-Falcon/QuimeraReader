import sys
import re

path = 'QuimeraReader.Application/Books/Queries/GetBooks/GetBooksQueryHandler.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the OrderByDescending
replacement = '''
        if (!string.IsNullOrWhiteSpace(request.ReadingStatus))
        {
            query = query.OrderByDescending(b => b.LastReadAt).ThenByDescending(b => b.Id);
        }
        else
        {
            query = query.OrderByDescending(b => b.Id);
        }

        var booksList = await query
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .Include(b => b.Series).ThenInclude(s => s.Universe)
            .Skip((page - 1) * pageSize)
'''

content = re.sub(r'        var booksList = await query\s*\.Include[^\n]*\s*\.Include[^\n]*\s*\.Include[^\n]*\s*\.OrderByDescending\(b => b\.Id\)\s*\.Skip\(\(page - 1\) \* pageSize\)', replacement.strip(), content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)