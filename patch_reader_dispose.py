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
    
    # Clean up the bad public void Dispose() that I added before
    import re
    content = re.sub(r'public void Dispose\(\)\s*\{\s*_syncMapTimer\?\.Dispose\(\);\s*// Guardado de emergencia al salir\s*if \(Book != null && !string\.IsNullOrEmpty\(Book\.CurrentEpubCfi\)\)\s*\{\s*_ = Task\.Run\(async \(\) => \{\s*try \{\s*await BookService\.UpdatePositionAsync[^}]+\}\s*catch \{ \}\s*\}\);\s*\}\s*\}', '', content)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')