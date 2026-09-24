import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_save = '''    private async Task SaveAnnotationAsync()
    {
        _showAnnotationDialog = false;
        if (_epubJsModule != null && !string.IsNullOrEmpty(_selectedCfi))
        {
            // Appy to viewer immediately for fast feedback
            string annType = string.IsNullOrEmpty(_selectedColor) ? "underline" : "highlight";
            string color = string.IsNullOrEmpty(_selectedColor) ? "#ffffff" : _selectedColor;
            await _epubJsModule.InvokeVoidAsync("applyAnnotation", _selectedCfi, annType, color);
            
            // TODO: Call API to persist to BookAnnotations
            // await BookService.CreateAnnotationAsync(BookId, new AnnotationDto { ... });
        }
    }'''

    new_save = '''    private async Task SaveAnnotationAsync()
    {
        _showAnnotationDialog = false;
        if (_epubJsModule != null && !string.IsNullOrEmpty(_selectedCfi))
        {
            bool hasNote = !string.IsNullOrWhiteSpace(_annotationNote);
            string color = _selectedColor;
            
            // If neither highlighted nor noted, don't do anything
            if (string.IsNullOrEmpty(color) && !hasNote) return;

            // Apply immediately to UI
            await _epubJsModule.InvokeVoidAsync("applyAnnotation", _selectedCfi, color, hasNote);
            
            // Persist
            try 
            {
                await BookService.CreateAnnotationAsync(BookId, _selectedCfi, _selectedText, color, _annotationNote);
                ToastService.ShowSuccess("Anotación guardada.");
            }
            catch(Exception ex)
            {
                ToastService.ShowError($"Error guardando anotación: {ex.Message}");
            }
        }
    }'''

    content = content.replace(old_save, new_save)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')