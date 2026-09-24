import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    new_ui = '''<div class="mt-4 pt-4" style="border-top: 1px solid rgba(255,255,255,0.1);">
                        <div class="d-flex justify-content-between align-items-center mb-4">
                            <div>
                                <h6 class="text-white mb-0"><i class="bi bi-cloud-arrow-down me-2"></i> Disponible sin conexión</h6>
                                <small class="text-white-50">Descarga los archivos a tu dispositivo para leer y escuchar sin internet.</small>
                            </div>
                            <RadzenSwitch @bind-Value="_book.IsAvailableOffline" Change="@ToggleOfflineAsync" />
                        </div>
                        <h6 class="text-white-50 mb-3"><i class="bi bi-gear me-2"></i> Gestión de archivos</h6>'''

    content = content.replace(
        '<div class="mt-4 pt-4" style="border-top: 1px solid rgba(255,255,255,0.1);">\n                        <h6 class="text-white-50 mb-3"><i class="bi bi-gear me-2"></i> Gestión de archivos</h6>',
        new_ui
    )

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/Pages/BookDetails.razor')