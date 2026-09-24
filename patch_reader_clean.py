import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_dispose = '''    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        if (_jsModule != null)
        {
            await _jsModule.DisposeAsync();
        }
        if (_epubJsModule != null)
        {
            await _epubJsModule.DisposeAsync();
        }
    }'''

    new_dispose = '''    public async ValueTask DisposeAsync()
    {
        // Guardado de emergencia al salir
        if (Book != null && !string.IsNullOrEmpty(Book.CurrentEpubCfi))
        {
            try {
                await BookService.UpdatePositionAsync(BookId, epubCfi: Book.CurrentEpubCfi, audioPosition: _currentTime, percentage: Book.PercentageCompleted);
            } catch { }
        }

        _dotNetRef?.Dispose();
        if (_jsModule != null)
        {
            await _jsModule.DisposeAsync();
        }
        if (_epubJsModule != null)
        {
            await _epubJsModule.DisposeAsync();
        }
    }'''
    
    content = content.replace(old_dispose, new_dispose)
    
    old_update = '''        _ = BookService.UpdatePositionAsync(BookId, epubCfi: cfi, percentage: scaledPercentage);'''
    new_update = '''        _ = System.Threading.Tasks.Task.Run(async () => {
            try {
                await BookService.UpdatePositionAsync(BookId, epubCfi: cfi, percentage: scaledPercentage);
            } catch { }
        });'''
    content = content.replace(old_update, new_update)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')