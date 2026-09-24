import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_modal = '''<div class="d-flex justify-content-end gap-2">
                <button class="btn btn-secondary btn-sm" @onclick="CloseAnnotationDialog">Cancelar</button>
                <button class="btn btn-primary btn-sm" @onclick="SaveAnnotationAsync">Guardar</button>
            </div>'''
            
    new_modal = '''<div class="d-flex justify-content-between gap-2">
                @if (Book.HasAudio && _segments.Any())
                {
                    <button class="btn btn-info btn-sm text-white" @onclick="PlayFromSelectionAsync"><i class="bi bi-play-circle me-1"></i> Reproducir</button>
                }
                else
                {
                    <div></div>
                }
                <div class="d-flex gap-2">
                    <button class="btn btn-secondary btn-sm" @onclick="CloseAnnotationDialog">Cancelar</button>
                    <button class="btn btn-primary btn-sm" @onclick="SaveAnnotationAsync">Guardar</button>
                </div>
            </div>'''
            
    content = content.replace(old_modal, new_modal)

    # Add PlayFromSelectionAsync method
    methods = '''    private async Task SaveAnnotationAsync()'''
    new_methods = '''    private async Task PlayFromSelectionAsync()
    {
        _showAnnotationDialog = false;
        if (!string.IsNullOrEmpty(_selectedText) && _segments.Any())
        {
            // Normalize selected text
            var searchStr = new string(_selectedText.ToLower().Where(c => char.IsLetterOrDigit(c)).ToArray());
            
            // Find in segments
            var match = _segments.FirstOrDefault(s => {
                var sText = new string(s.Text.ToLower().Where(c => char.IsLetterOrDigit(c)).ToArray());
                return sText.Contains(searchStr) || searchStr.Contains(sText);
            });

            if (match != null)
            {
                await SeekToSegmentAsync(match.Start);
                ToastService.ShowSuccess("Reproduciendo desde la selección.");
            }
            else
            {
                ToastService.ShowError("No se encontró el audio para este fragmento.");
            }
        }
    }

    private async Task SaveAnnotationAsync()'''
    
    content = content.replace(methods, new_methods)
    
    # Add highlightKaraokePhrase to OnTimeUpdate
    time_update_old = '''            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("scrollToActiveSegment");
            }'''
            
    time_update_new = '''            if (_epubJsModule != null)
            {
                await _epubJsModule.InvokeVoidAsync("highlightKaraokePhrase", _segments[currentIndex].Text);
            }
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("scrollToActiveSegment");
            }'''
            
    content = content.replace(time_update_old, time_update_new)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')