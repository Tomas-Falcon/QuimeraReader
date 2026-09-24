import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

old_delete = '''    private async Task DeleteSelected()
    {
        if (_selectedBookIds.Count == 0) return;
        
        foreach (var id in _selectedBookIds.ToList())
        {
            try
            {
                await BookService.DeleteEpubAsync(id);
                
            }
            catch (Exception ex)
            {
                ToastService.ShowError($"Error al borrar libro {id}: {ex.Message}");
            }
        }
        
        _selectedBookIds.Clear();
        _selectionMode = false;
        ToastService.ShowSuccess("Libros borrados correctamente");
        if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync(); StateHasChanged();
    }'''

new_delete = '''    private async Task DeleteSelected()
    {
        if (_selectedBookIds.Count == 0) return;
        
        var result = await DialogService.Confirm(
            $"ATENCION! Vas a borrar {_selectedBookIds.Count} libros. Esta accion ELIMINARA PERMANENTEMENTE los archivos fisicos (EPUBs, audios, portadas).", 
            "Eliminar Libros", 
            new ConfirmOptions() { OkButtonText = "Si, ELIMINAR", CancelButtonText = "Cancelar" });

        if (result == true)
        {
            try
            {
                await BookService.DeleteBooksAsync(_selectedBookIds.ToArray());
                _selectedBookIds.Clear();
                _selectionMode = false;
                ToastService.ShowSuccess("Libros borrados correctamente de forma permanente");
                if (_virtualizeComponent != null) await _virtualizeComponent.RefreshDataAsync(); 
                StateHasChanged();
            }
            catch (Exception ex)
            {
                ToastService.ShowError($"Error al borrar libros: {ex.Message}");
            }
        }
    }'''

content = content.replace(old_delete, new_delete)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)