import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_progress = '''<!-- Progress Footer -->
                    <div class="reading-progress text-center py-2" style="background: var(--bg-card); font-size: 0.85rem; color: var(--text-muted);">
                        @if (Book.PercentageCompleted.HasValue)
                        {
                            <span>@($"{Math.Round(Book.PercentageCompleted.Value, 1)}% leído")</span>
                            @if (_totalPages > 0)
                            {
                                <span class="ms-2 opacity-75">| Página @_currentPage de @_totalPages</span>
                            }
                        }
                    </div>'''

    new_progress = '''<!-- Progress Footer (Kindle Style) -->
                    <div class="reading-progress px-4 py-2" style="background: var(--bg-card); font-size: 0.85rem; color: var(--text-muted); border-top: 1px solid rgba(255,255,255,0.1);">
                        <div class="d-flex align-items-center justify-content-between mb-1" style="font-size: 0.75rem;">
                            <span class="fw-bold text-white-50">@(_totalPages > 0 ? $"Pág. {_currentPage}" : "")</span>
                            <span class="fw-bold text-white-50">@(_totalPages > 0 ? $"{_totalPages} páginas -" : "") @($"{Math.Round(Book.PercentageCompleted ?? 0, 1)}%")</span>
                        </div>
                        <input type="range" class="form-range custom-progress-bar" min="0" max="100" step="0.1" value="@(Book.PercentageCompleted ?? 0)" @onchange="OnProgressSliderChanged" />
                    </div>'''

    content = content.replace(old_progress, new_progress)

    # We also need to add OnProgressSliderChanged to the @code block
    code_block = '''private async Task PrevPage()
    {
        if (_epubJsModule != null)
            await _epubJsModule.InvokeVoidAsync("prevEpubPage");
    }'''

    new_code = code_block + '''

    private async Task OnProgressSliderChanged(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double newPct))
        {
            if (_epubJsModule != null)
            {
                await _epubJsModule.InvokeVoidAsync("goToPercentage", newPct);
            }
        }
    }'''

    content = content.replace(code_block, new_code)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')