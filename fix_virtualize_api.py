import os
import re

# 1. GetBooksQuery.cs
path = 'QuimeraReader.Application/Books/Queries/GetBooks/GetBooksQuery.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace('public int Page { get; set; } = 1;', 'public int Page { get; set; } = 1;\n    public int? Skip { get; set; }\n    public int? Take { get; set; }')
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

# 2. GetBooksQueryHandler.cs
path = 'QuimeraReader.Application/Books/Queries/GetBooks/GetBooksQueryHandler.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace('int page = request.Page < 1 ? 1 : request.Page;\n        int pageSize = request.PageSize < 1 ? 50 : (request.PageSize > 100 ? 100 : request.PageSize);', 'int page = request.Page < 1 ? 1 : request.Page;\n        int pageSize = request.PageSize < 1 ? 50 : (request.PageSize > 1000 ? 1000 : request.PageSize);\n        int skip = request.Skip ?? ((page - 1) * pageSize);\n        int take = request.Take ?? pageSize;')
content = content.replace('.Skip((page - 1) * pageSize)\n            .Take(pageSize)', '.Skip(skip)\n            .Take(take)')
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

# 3. BooksController.cs
path = 'QuimeraReader.API/Controllers/BooksController.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace('public async Task<IActionResult> GetBooks([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] int[]? categoryIds = null, [FromQuery] string? readingStatus = null)', 'public async Task<IActionResult> GetBooks([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] int[]? categoryIds = null, [FromQuery] string? readingStatus = null, [FromQuery] int? skip = null, [FromQuery] int? take = null)')
content = content.replace('PageSize = pageSize, \n                Search = search', 'PageSize = pageSize, \n                Skip = skip,\n                Take = take,\n                Search = search')
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

# 4. BookService.cs
path = 'QuimeraReader.Clients/QuimeraReader.Shared/Services/BookService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace('Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null);', 'Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null);')
content = content.replace('public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null)', 'public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null)')
content = content.replace('var url = $"api/Books?page={page}&pageSize={pageSize}";', 'var url = $"api/Books?page={page}&pageSize={pageSize}";\n            if (skip.HasValue) url += $"&skip={skip.Value}";\n            if (take.HasValue) url += $"&take={take.Value}";')
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)