import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Services/BookService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add to interface
content = content.replace('Task DeleteEpubAsync(int bookId);', 'Task DeleteEpubAsync(int bookId);\n    Task DeleteBooksAsync(int[] ids);')

# Add implementation
impl = '''    public async Task DeleteBooksAsync(int[] ids)
    {
        var queryString = string.Join("&", ids.Select(id => $"ids={id}"));
        var response = await _httpClient.DeleteAsync($"api/Books?{queryString}");
        response.EnsureSuccessStatusCode();
    }
'''
content = content.replace('public async Task DeleteEpubAsync(int bookId)', impl + '\n    public async Task DeleteEpubAsync(int bookId)')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)