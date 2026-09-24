import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

replacement = '''
        try
        {
            var readingTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "Reading");
            var nextToReadTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "NextToRead");
            var readTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "Read");
            
            await Task.WhenAll(readingTask, nextToReadTask, readTask);
            
            _readingBooks = readingTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();
            _nextToReadBooks = nextToReadTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();
            _readBooks = readTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();
        }
'''

content = re.sub(r'var initialBooks = await BookService\.GetBooksAsync\(1, 1000, ""\);\s*_readingBooks.*?\s*_nextToReadBooks.*?\s*_readBooks.*?;', replacement.strip(), content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)