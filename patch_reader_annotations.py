import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    ui_layer = '''
    @if (_showAnnotationDialog)
    {
        <div class="annotation-dialog position-fixed" style="bottom: 80px; left: 50%; transform: translateX(-50%); background: var(--bg-card); padding: 15px; border-radius: 12px; box-shadow: 0 10px 30px rgba(0,0,0,0.5); z-index: 9999; width: 90%; max-width: 400px; border: 1px solid rgba(255,255,255,0.1);">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <h6 class="mb-0 text-white"><i class="bi bi-pencil-square me-2"></i> Nueva Anotación</h6>
                <button class="btn-close btn-close-white" @onclick="CloseAnnotationDialog"></button>
            </div>
            <p class="text-white-50 small mb-3 text-truncate" style="font-style: italic;">"@_selectedText"</p>
            
            <div class="mb-3">
                <label class="form-label text-white-50 small">Color de resaltado</label>
                <div class="d-flex gap-2">
                    <button class="btn btn-sm rounded-circle" style="width: 30px; height: 30px; background-color: #ffeb3b; border: @(_selectedColor == "#ffeb3b" ? "2px solid white" : "none")" @onclick="@(() => _selectedColor = "#ffeb3b")"></button>
                    <button class="btn btn-sm rounded-circle" style="width: 30px; height: 30px; background-color: #4caf50; border: @(_selectedColor == "#4caf50" ? "2px solid white" : "none")" @onclick="@(() => _selectedColor = "#4caf50")"></button>
                    <button class="btn btn-sm rounded-circle" style="width: 30px; height: 30px; background-color: #2196f3; border: @(_selectedColor == "#2196f3" ? "2px solid white" : "none")" @onclick="@(() => _selectedColor = "#2196f3")"></button>
                    <button class="btn btn-sm rounded-circle" style="width: 30px; height: 30px; background-color: #f44336; border: @(_selectedColor == "#f44336" ? "2px solid white" : "none")" @onclick="@(() => _selectedColor = "#f44336")"></button>
                    <button class="btn btn-sm rounded-circle" style="width: 30px; height: 30px; background-color: transparent; border: 1px dashed rgba(255,255,255,0.5);" @onclick="@(() => _selectedColor = "")" title="Sin color (Solo subrayado)"></button>
                </div>
            </div>

            <div class="mb-3">
                <label class="form-label text-white-50 small">Nota (Opcional)</label>
                <textarea class="form-control bg-dark text-white border-secondary" @bind="_annotationNote" rows="2" placeholder="Escribe un comentario..."></textarea>
            </div>

            <div class="d-flex justify-content-end gap-2">
                <button class="btn btn-secondary btn-sm" @onclick="CloseAnnotationDialog">Cancelar</button>
                <button class="btn btn-primary btn-sm" @onclick="SaveAnnotationAsync">Guardar</button>
            </div>
        </div>
    }
'''
    
    content = content.replace('<!-- Native Player', ui_layer + '\n            <!-- Native Player')

    # Add code block variables and methods
    methods = '''
    private bool _showAnnotationDialog = false;
    private string _selectedCfi = "";
    private string _selectedText = "";
    private string _selectedColor = "#ffeb3b";
    private string _annotationNote = "";

    [JSInvokable]
    public void OnEpubTextSelected(string cfiRange, string text)
    {
        _selectedCfi = cfiRange;
        _selectedText = text;
        _selectedColor = "#ffeb3b";
        _annotationNote = "";
        _showAnnotationDialog = true;
        StateHasChanged();
    }

    private void CloseAnnotationDialog()
    {
        _showAnnotationDialog = false;
        StateHasChanged();
    }

    private async Task SaveAnnotationAsync()
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
    }
'''

    content = content.replace('private async Task PrevPage()', methods + '\n    private async Task PrevPage()')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')