import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    karaoke_funcs = '''
export function highlightKaraokePhrase(text) {
    if (!window.epubRendition) return;
    
    var contents = window.epubRendition.getContents();
    if (!contents || contents.length === 0) return;
    var doc = contents[0].document;
    
    // Remove previous highlights
    var prev = doc.querySelectorAll('.karaoke-highlight');
    prev.forEach(el => {
        var parent = el.parentNode;
        parent.replaceChild(doc.createTextNode(el.textContent), el);
        parent.normalize();
    });

    if (!text || text.trim().length === 0) return;
    
    // Normalize string for fuzzy matching (Whisper text vs EPUB text)
    var searchStr = text.toLowerCase().replace(/[^a-z0-9áéíóúñ]/gi, '').trim();
    if(searchStr.length < 5) return; // Too short to accurately match

    var treeWalker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT, null, false);
    var currentNode = treeWalker.nextNode();
    var matchFound = false;

    while (currentNode && !matchFound) {
        var nodeText = currentNode.nodeValue;
        var nodeTextNorm = nodeText.toLowerCase().replace(/[^a-z0-9áéíóúñ]/gi, '');
        
        // Simple subset matching for now (Whisper sentence often fits inside a paragraph's text node)
        if (nodeTextNorm.includes(searchStr) || searchStr.includes(nodeTextNorm)) {
            // Found a text node that contains the text (or viceversa). 
            // We highlight the whole node for simplicity if it's a good chunk, or we can use mark.js
            // Let's just wrap the node's parent if it's small, or use a RegExp if it contains it.
            try {
                // very rough highlight of the parent element
                if (currentNode.parentNode && currentNode.parentNode.tagName !== 'SCRIPT' && currentNode.parentNode.tagName !== 'STYLE') {
                    currentNode.parentNode.classList.add('karaoke-highlight');
                    currentNode.parentNode.style.backgroundColor = 'rgba(255, 193, 7, 0.4)';
                    currentNode.parentNode.style.borderRadius = '4px';
                    currentNode.parentNode.style.transition = 'background-color 0.3s';
                    
                    // Optional: scroll into view
                    // currentNode.parentNode.scrollIntoView({behavior: "smooth", block: "center"});
                }
            } catch(e){}
            matchFound = true;
        }
        currentNode = treeWalker.nextNode();
    }
}
'''
    
    content += karaoke_funcs

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js')