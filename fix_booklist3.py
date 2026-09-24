import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Update HTML parts for Reading
reading_html_old = '''<div class="netflix-row mb-5">
                @foreach (var book in _readingBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
            </div>'''
reading_html_new = '''<div class="netflix-row mb-5">
                @foreach (var book in _readingBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
                @if (_hasMoreReading)
                {
                    <div @ref="_loadMoreReadingElement" class="netflix-item d-flex align-items-center justify-content-center" style="width: 100px;">
                        <RadzenProgressBarCircular ShowValue="false" Mode="ProgressBarMode.Indeterminate" Size="ProgressBarCircularSize.Medium" />
                    </div>
                }
            </div>'''
content = content.replace(reading_html_old, reading_html_new)

# Update NextToRead
next_html_old = '''<div class="netflix-row mb-5">
                @foreach (var book in _nextToReadBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
            </div>'''
next_html_new = '''<div class="netflix-row mb-5">
                @foreach (var book in _nextToReadBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
                @if (_hasMoreNextToRead)
                {
                    <div @ref="_loadMoreNextToReadElement" class="netflix-item d-flex align-items-center justify-content-center" style="width: 100px;">
                        <RadzenProgressBarCircular ShowValue="false" Mode="ProgressBarMode.Indeterminate" Size="ProgressBarCircularSize.Medium" />
                    </div>
                }
            </div>'''
content = content.replace(next_html_old, next_html_new)

# Update Read
read_html_old = '''<div class="netflix-row mb-5">
                @foreach (var book in _readBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
            </div>'''
read_html_new = '''<div class="netflix-row mb-5">
                @foreach (var book in _readBooks)
                {
                    <div class="netflix-item"><BookCard Book="book" /></div>
                }
                @if (_hasMoreRead)
                {
                    <div @ref="_loadMoreReadElement" class="netflix-item d-flex align-items-center justify-content-center" style="width: 100px;">
                        <RadzenProgressBarCircular ShowValue="false" Mode="ProgressBarMode.Indeterminate" Size="ProgressBarCircularSize.Medium" />
                    </div>
                }
            </div>'''
content = content.replace(read_html_old, read_html_new)

# 2. Add properties
props = '''    private List<Book> _books = new();
    
    // Pagination fields for carousels
    private List<Book> _readingBooks = new();
    private int _readingPage = 1;
    private bool _hasMoreReading = false;
    private bool _isLoadingReading = false;
    private ElementReference _loadMoreReadingElement;
    private bool _readingScrollInitialized = false;

    private List<Book> _nextToReadBooks = new();
    private int _nextToReadPage = 1;
    private bool _hasMoreNextToRead = false;
    private bool _isLoadingNextToRead = false;
    private ElementReference _loadMoreNextToReadElement;
    private bool _nextToReadScrollInitialized = false;

    private List<Book> _readBooks = new();
    private int _readPage = 1;
    private bool _hasMoreRead = false;
    private bool _isLoadingRead = false;
    private ElementReference _loadMoreReadElement;
    private bool _readScrollInitialized = false;'''
content = re.sub(r'    private List<Book> _books = new\(\);', props, content)

# Modify the initial load to set _hasMore correctly
init_load_old = '''            var readingTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "Reading");
            var nextToReadTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "NextToRead");
            var readTask = BookService.GetBooksAsync(1, 30, "", readingStatus: "Read");
            
            await Task.WhenAll(readingTask, nextToReadTask, readTask);
            
            _readingBooks = readingTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();
            _nextToReadBooks = nextToReadTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();
            _readBooks = readTask.Result?.Data?.OrderByDescending(b => b.LastReadAt).ToList() ?? new List<Book>();'''
init_load_new = '''            var readingTask = BookService.GetBooksAsync(1, 20, "", readingStatus: "Reading");
            var nextToReadTask = BookService.GetBooksAsync(1, 20, "", readingStatus: "NextToRead");
            var readTask = BookService.GetBooksAsync(1, 20, "", readingStatus: "Read");
            
            await Task.WhenAll(readingTask, nextToReadTask, readTask);
            
            _readingBooks = readingTask.Result?.Data?.ToList() ?? new List<Book>();
            _hasMoreReading = readingTask.Result?.Data?.Length >= 20;

            _nextToReadBooks = nextToReadTask.Result?.Data?.ToList() ?? new List<Book>();
            _hasMoreNextToRead = nextToReadTask.Result?.Data?.Length >= 20;

            _readBooks = readTask.Result?.Data?.ToList() ?? new List<Book>();
            _hasMoreRead = readTask.Result?.Data?.Length >= 20;'''
content = content.replace(init_load_old, init_load_new)

# Add OnAfterRender logic
after_render_old = '''    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_hasMore && _loadMoreElement.Id != null)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("infiniteScroll.initialize", _loadMoreElement, _objRef);
            }
            catch { }
        }
    }'''
after_render_new = '''    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (_hasMore && _loadMoreElement.Id != null)
                await JSRuntime.InvokeVoidAsync("infiniteScroll.initialize", _loadMoreElement, _objRef, "LoadMoreRequested");

            if (_hasMoreReading && _loadMoreReadingElement.Id != null && !_readingScrollInitialized)
            {
                await JSRuntime.InvokeVoidAsync("infiniteScroll.initialize", _loadMoreReadingElement, _objRef, "LoadMoreReadingRequested");
                _readingScrollInitialized = true;
            }
            if (_hasMoreNextToRead && _loadMoreNextToReadElement.Id != null && !_nextToReadScrollInitialized)
            {
                await JSRuntime.InvokeVoidAsync("infiniteScroll.initialize", _loadMoreNextToReadElement, _objRef, "LoadMoreNextToReadRequested");
                _nextToReadScrollInitialized = true;
            }
            if (_hasMoreRead && _loadMoreReadElement.Id != null && !_readScrollInitialized)
            {
                await JSRuntime.InvokeVoidAsync("infiniteScroll.initialize", _loadMoreReadElement, _objRef, "LoadMoreReadRequested");
                _readScrollInitialized = true;
            }
        }
        catch { }
    }'''
content = content.replace(after_render_old, after_render_new)

# Add JSInvokable methods
methods = '''
    [JSInvokable]
    public async Task LoadMoreReadingRequested()
    {
        if (_isLoadingReading || !_hasMoreReading) return;
        _isLoadingReading = true; StateHasChanged();
        try
        {
            _readingPage++;
            var result = await BookService.GetBooksAsync(_readingPage, 20, "", readingStatus: "Reading");
            if (result.Data.Length > 0) _readingBooks.AddRange(result.Data);
            _hasMoreReading = result.Data.Length >= 20;
            _readingScrollInitialized = false; // re-init on next render if needed
        }
        catch { }
        finally { _isLoadingReading = false; StateHasChanged(); }
    }

    [JSInvokable]
    public async Task LoadMoreNextToReadRequested()
    {
        if (_isLoadingNextToRead || !_hasMoreNextToRead) return;
        _isLoadingNextToRead = true; StateHasChanged();
        try
        {
            _nextToReadPage++;
            var result = await BookService.GetBooksAsync(_nextToReadPage, 20, "", readingStatus: "NextToRead");
            if (result.Data.Length > 0) _nextToReadBooks.AddRange(result.Data);
            _hasMoreNextToRead = result.Data.Length >= 20;
            _nextToReadScrollInitialized = false;
        }
        catch { }
        finally { _isLoadingNextToRead = false; StateHasChanged(); }
    }

    [JSInvokable]
    public async Task LoadMoreReadRequested()
    {
        if (_isLoadingRead || !_hasMoreRead) return;
        _isLoadingRead = true; StateHasChanged();
        try
        {
            _readPage++;
            var result = await BookService.GetBooksAsync(_readPage, 20, "", readingStatus: "Read");
            if (result.Data.Length > 0) _readBooks.AddRange(result.Data);
            _hasMoreRead = result.Data.Length >= 20;
            _readScrollInitialized = false;
        }
        catch { }
        finally { _isLoadingRead = false; StateHasChanged(); }
    }

    [JSInvokable]
    public async Task LoadMoreRequested()
'''
content = content.replace('    [JSInvokable]\n    public async Task LoadMoreRequested()', methods)

# Also remove old properties
content = re.sub(r'    private IEnumerable<Book> _readBooks = Array.Empty<Book>\(\);\n    private IEnumerable<Book> _readingBooks = Array.Empty<Book>\(\);\n', '', content)
content = re.sub(r'    private IEnumerable<QuimeraReader.Shared.Models.Book> _nextToReadBooks = Array.Empty<QuimeraReader.Shared.Models.Book>\(\);\n', '', content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)