import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    selected_event = '''
    rendition.on("selected", function(cfiRange, contents) {
        book.getRange(cfiRange).then(function(range) {
            var text = range.toString();
            if(text && text.trim().length > 0) {
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnEpubTextSelected", cfiRange, text).catch(e => console.warn(e));
                }
            }
        });
    });
'''

    content = content.replace('rendition.on("relocated", function (location) {', selected_event + '\n    rendition.on("relocated", function (location) {')

    # Add exports for applying highlights/underlines
    exports = '''
export function applyAnnotation(cfiRange, type, color) {
    if (window.epubRendition) {
        if (type === "highlight") {
            window.epubRendition.annotations.highlight(cfiRange, {}, (e) => {
                console.log("Highlight clicked", e);
            }, "", {"fill": color, "fill-opacity": "0.3"});
        } else if (type === "underline") {
            window.epubRendition.annotations.underline(cfiRange, {}, (e) => {
                console.log("Underline clicked", e);
            }, "", {"stroke": color, "stroke-width": "2px", "stroke-opacity": "0.8"});
        }
    }
}
'''
    content += exports

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js')