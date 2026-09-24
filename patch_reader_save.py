import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Improve ReaderPlayer saving logic
    old_csharp = '''    [JSInvokable]
    public void OnEpubLocationChanged(string cfi, double percentage, int currentPage = 0, int totalPages = 0)
    {
        // Guardar el CFI y el porcentaje
        double? scaledPercentage = percentage >= 0 ? (percentage * 100.0) : null;
        
        if (Book != null)
        {
            Book.CurrentEpubCfi = cfi;
            if (scaledPercentage.HasValue) 
                Book.PercentageCompleted = scaledPercentage.Value;
            if (totalPages > 0) Book.TotalPages = totalPages;
            if (currentPage > 0) { _currentPage = currentPage; _totalPages = totalPages; }
            StateHasChanged();
        }

        _ = BookService.UpdatePositionAsync(BookId, epubCfi: cfi, percentage: scaledPercentage);
    }'''

    new_csharp = '''    [JSInvokable]
    public void OnEpubLocationChanged(string cfi, double percentage, int currentPage = 0, int totalPages = 0)
    {
        // Guardar el CFI y el porcentaje
        double? scaledPercentage = percentage >= 0 ? (percentage * 100.0) : null;
        
        if (Book != null)
        {
            Book.CurrentEpubCfi = cfi;
            if (scaledPercentage.HasValue) 
                Book.PercentageCompleted = scaledPercentage.Value;
            if (totalPages > 0) Book.TotalPages = totalPages;
            if (currentPage > 0) { _currentPage = currentPage; _totalPages = totalPages; }
            StateHasChanged();
        }

        // Fire and forget, pero atrapamos errores para no romper el hilo UI
        _ = Task.Run(async () => {
            try {
                await BookService.UpdatePositionAsync(BookId, epubCfi: cfi, percentage: scaledPercentage);
            } catch (Exception ex) {
                Console.WriteLine($""Error guardando progreso: {ex.Message}"");
            }
        });
    }

    public void Dispose()
    {
        _syncMapTimer?.Dispose();
        _audioElement.Dispose();
        
        // Guardado de emergencia al salir
        if (Book != null && !string.IsNullOrEmpty(Book.CurrentEpubCfi))
        {
            _ = Task.Run(async () => {
                try {
                    await BookService.UpdatePositionAsync(BookId, epubCfi: Book.CurrentEpubCfi, audioPosition: _currentTime, percentage: Book.PercentageCompleted);
                } catch { }
            });
        }
    }'''

    # We need to replace the exact block.
    # Since Dispose might already exist, let's find if it exists.
    if 'public void Dispose()' in content:
        # replace existing Dispose
        dispose_regex = r'public void Dispose\(\)\s*\{[^\}]+\}'
        import re
        content = re.sub(dispose_regex, '''public void Dispose()
    {
        _syncMapTimer?.Dispose();
        
        // Guardado de emergencia al salir
        if (Book != null && !string.IsNullOrEmpty(Book.CurrentEpubCfi))
        {
            _ = Task.Run(async () => {
                try {
                    await BookService.UpdatePositionAsync(BookId, epubCfi: Book.CurrentEpubCfi, audioPosition: _currentTime, percentage: Book.PercentageCompleted);
                } catch { }
            });
        }
    }''', content)
        content = content.replace(old_csharp, new_csharp.split('public void Dispose()')[0])
    else:
        content = content.replace(old_csharp, new_csharp)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')