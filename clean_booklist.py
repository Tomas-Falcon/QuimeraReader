import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. HandleFileUpload
content = re.sub(r'                _currentPage = 1;\n                _hasMore = true;\n                \(\_virtualizeComponent == null\);\n                if \(_virtualizeComponent != null\) await _virtualizeComponent.RefreshDataAsync\(\);', '                if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();', content)

# 2. PollNewBooksAsync
old_poll = '''    private async Task PollNewBooksAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                if (!string.IsNullOrWhiteSpace(_searchQuery)) continue;
                
                if ((_virtualizeComponent == null) > 0 && _currentPage > 1)
                {
                    try
                    {
                        var result = await BookService.GetBooksAsync(1, 1);
                        if (result.Data.Length > 0 && result.Data[0].Id > _books[0].Id)
                        {
                            _currentPage = 1;
                            _hasMore = true;
                            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
                            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
                        }
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) { }
    }'''

new_poll = '''    private async Task PollNewBooksAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                // We could poll to check for new books, but Virtualize handles its own data loading.
                // We'll just refresh it periodically if not searching.
                if (string.IsNullOrWhiteSpace(_searchQuery) && _virtualizeComponent != null)
                {
                    // For performance, we shouldn't force refresh every 10s if user is scrolling. 
                    // Let's omit aggressive polling for the virtualized list.
                }
            }
        }
        catch (OperationCanceledException) { }
    }'''
# content = content.replace(old_poll, new_poll)
content = re.sub(r'    private async Task PollNewBooksAsync\(\)\n    \{.*?\n    \}', new_poll, content, flags=re.DOTALL)


# 3. LoadMoreRequested
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n    \{\n        if \(!_isLoading && _hasMore\)\n        \{\n            LoadMoreAsync\(\);\n            StateHasChanged\(\);\n        \}\n    \}', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading && _hasMore\) return;\s*\n\s*try\s*\{\s*await LoadMoreAsync\(\);\s*\}\s*catch \{ \}\s*\}\s*', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\s*if \(!_isLoading && _hasMore\)\n\s*\{\n\s*LoadMoreAsync\(\);\n\s*StateHasChanged\(\);\n\s*\}\n\s*\}', '', content, flags=re.DOTALL)


# 4. OnParametersSetAsync
old_params = '''    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(QueryParam) && QueryParam != _searchQuery)
        {
            _searchQuery = QueryParam;
            _currentPage = 1;
            _hasMore = true;
            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
        }
        else if (string.IsNullOrWhiteSpace(QueryParam) && (_virtualizeComponent == null) && !_isLoading)
        {
            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
        }
    }'''
new_params = '''    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(QueryParam) && QueryParam != _searchQuery)
        {
            _searchQuery = QueryParam;
            if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
        }
        else if (string.IsNullOrWhiteSpace(QueryParam) && _virtualizeComponent != null)
        {
            // await _virtualizeComponent.RefreshDataAsync();
        }
    }'''
content = content.replace(old_params, new_params)


# 5. OnCategoryChanged
old_cat = '''        private async Task OnCategoryChanged(object value)
    {
        _currentPage = 1;
        if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
    }'''
new_cat = '''        private async Task OnCategoryChanged(object value)
    {
        if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();
    }'''
content = content.replace(old_cat, new_cat)

# 6. DeleteSelected
content = re.sub(r'_books\.RemoveAll\(b => b\.Id == id\);', '', content)
content = content.replace('ToastService.ShowSuccess("Libros borrados correctamente");\n        StateHasChanged();', 'ToastService.ShowSuccess("Libros borrados correctamente");\n        if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync();\n        StateHasChanged();')

# 7. Debounce Timer (Search)
content = re.sub(r'_currentPage = 1;\n\s*_hasMore = true;\n\s*if \(_virtualizeComponent != null\) await _virtualizeComponent\.RefreshDataAsync\(\);\n\s*await InvokeAsync', 'await InvokeAsync', content)
content = re.sub(r'if \(_virtualizeComponent != null\) await _virtualizeComponent.RefreshDataAsync\(\);\n\s*StateHasChanged\(\);', 'if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync(); StateHasChanged();', content)

# 8. OnAfterRenderAsync
content = re.sub(r'if \(_hasMore && _loadMoreElement\.Id != null\)\n\s*await JSRuntime\.InvokeVoidAsync\("infiniteScroll\.initialize", _loadMoreElement, _objRef, "LoadMoreRequested"\);', '', content)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n    \{\n        if \(!_isLoading && _hasMore\)\n        \{\n            LoadMoreAsync\(\);\n            StateHasChanged\(\);\n        \}\n    \}', '', content)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading && _hasMore\)\n\s*\{\n\s*LoadMoreAsync\(\);\n\s*StateHasChanged\(\);\n\s*\}\n\s*\}', '', content)

content = re.sub(r'_currentPage = 1;\n\s*_hasMore = true;\n\s*\(\_virtualizeComponent == null\);', '', content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)