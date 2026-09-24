import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    window_assigns = '''
    window.epubBook = book;
    window.epubRendition = rendition;
    window.epubDotNetRef = dotNetRef;
'''
    
    content = content.replace('window.epubNext = () => { try { rendition.next(); } catch (e) { } };', window_assigns + '\n    window.epubNext = () => { try { rendition.next(); } catch (e) { } };')

    # Add exports
    exports = '''
export function goToPercentage(pct) {
    if (window.epubBook && window.epubBook.locations && window.epubBook.locations.length > 0) {
        var cfi = window.epubBook.locations.cfiFromPercentage(pct / 100.0);
        if (cfi && window.epubRendition) {
            window.epubRendition.display(cfi);
        }
    }
}
'''
    content += exports

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js')