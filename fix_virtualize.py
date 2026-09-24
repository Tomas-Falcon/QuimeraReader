import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the main grid with Virtualize
old_grid = '''        <div class="row m-0 row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-4 row-cols-xl-5 g-4">
            @foreach (var book in _books)
            {
                <div class="col"><BookCard Book="book" SelectionMode="_selectionMode" IsSelected="_selectedBookIds.Contains(book.Id)" IsSelectedChanged="@(isSelected => OnBookSelectionChanged(book.Id, isSelected))" /></div>
            }
        </div>
        
        @if (_hasMore)
        {
            <div @ref="_loadMoreElement" class="load-more-trigger mt-4 text-center p-3">
                @if (_isLoading)
                {
                    <RadzenProgressBarCircular ShowValue="false" Mode="ProgressBarMode.Indeterminate" Size="ProgressBarCircularSize.Medium" />
                }
            </div>
        }'''

new_grid = '''        <Microsoft.AspNetCore.Components.Web.Virtualize ItemsProvider="ProvideBookChunks" Context="chunk" OverscanCount="3" @ref="_virtualizeComponent">
            <ItemContent>
                <div class="row m-0 row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-4 row-cols-xl-5 g-4 mb-4">
                    @foreach (var book in chunk)
                    {
                        <div class="col">
                            <BookCard Book="book" SelectionMode="_selectionMode" IsSelected="_selectedBookIds.Contains(book.Id)" IsSelectedChanged="@(isSelected => OnBookSelectionChanged(book.Id, isSelected))" />
                        </div>
                    }
                </div>
            </ItemContent>
            <Placeholder>
                <div class="row m-0 row-cols-1 row-cols-sm-2 row-cols-md-3 row-cols-lg-4 row-cols-xl-5 g-4 mb-4">
                    @for (int i = 0; i < 5; i++)
                    {
                        <div class="col">
                            <div style="height: 350px; background: var(--bg-card); border-radius: 8px; opacity: 0.5;"></div>
                        </div>
                    }
                </div>
            </Placeholder>
        </Microsoft.AspNetCore.Components.Web.Virtualize>'''

content = content.replace(old_grid, new_grid)

# Add ProvideBookChunks and Virtualize reference
props = '''    private Microsoft.AspNetCore.Components.Web.Virtualize<List<Book>> _virtualizeComponent;

    private async ValueTask<Microsoft.AspNetCore.Components.Web.ItemsProviderResult<List<Book>>> ProvideBookChunks(Microsoft.AspNetCore.Components.Web.ItemsProviderRequest request)
    {
        int chunkSize = 5;
        int skip = request.StartIndex * chunkSize;
        int take = request.Count * chunkSize;

        var result = await BookService.GetBooksAsync(1, 50, _searchQuery, _selectedCategories?.ToArray(), null, skip, take);
        var totalChunks = (int)Math.Ceiling((double)result.Total / chunkSize);

        var chunks = new List<List<Book>>();
        for (int i = 0; i < result.Data.Length; i += chunkSize)
        {
            chunks.Add(result.Data.Skip(i).Take(chunkSize).ToList());
        }

        return new Microsoft.AspNetCore.Components.Web.ItemsProviderResult<List<Book>>(chunks, totalChunks);
    }
'''
content = content.replace('    private List<Book> _books = new();', props)

# Fix LoadBooksAsync and other references
# We no longer need _books, _hasMore, _isLoading, LoadBooksAsync, _loadMoreElement
content = re.sub(r'    private int _currentPage = 1;\n    private bool _isLoading = false;\n    private bool _hasMore = true;\n', '', content)
content = re.sub(r'    private ElementReference _loadMoreElement;\n', '', content)
content = content.replace('_books.Clear();', 'if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();')
content = content.replace('await LoadBooksAsync();', 'if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();')
content = content.replace('_books.Count == 0', '(_virtualizeComponent == null)')
content = content.replace('_books.RemoveAll(b => b.Id == id);', '')

# Remove LoadBooksAsync entirely
content = re.sub(r'    private async Task LoadBooksAsync\(\)\n    {.*?\n    }\n', '', content, flags=re.DOTALL)
content = re.sub(r'    private async Task LoadMoreAsync\(\)\n    {.*?\n    }\n', '', content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)