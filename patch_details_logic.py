import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    method = '''
    private async Task ToggleOfflineAsync(bool value)
    {
        try
        {
            await BookService.ToggleOfflineAvailabilityAsync(Id, value);
            ToastService.ShowSuccess(value ? "Libro marcado para leer sin conexión." : "Libro eliminado de descargas automáticas.");
        }
        catch (Exception ex)
        {
            ToastService.ShowError($"Error: {ex.Message}");
            // Revert state on failure
            _book.IsAvailableOffline = !value;
        }
    }
'''

    content = content.replace('private void GoToRead()\n    {\n        NavManager.NavigateTo($"/read/{Id}");\n    }', method + '\n    private void GoToRead()\n    {\n        NavManager.NavigateTo($"/read/{Id}");\n    }')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Pages/BookDetails.razor')