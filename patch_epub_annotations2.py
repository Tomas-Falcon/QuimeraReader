import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_exports = '''export function applyAnnotation(cfiRange, type, color) {
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
}'''

    new_exports = '''export function applyAnnotation(cfiRange, color, hasNote) {
    if (window.epubRendition) {
        if (color && color.length > 0) {
            window.epubRendition.annotations.highlight(cfiRange, {}, (e) => {
                console.log("Highlight clicked", e);
            }, "", {"fill": color, "fill-opacity": "0.3"});
        }
        if (hasNote) {
            var underlineColor = (color && color.length > 0) ? color : "#ffffff";
            window.epubRendition.annotations.underline(cfiRange, {}, (e) => {
                console.log("Underline clicked", e);
            }, "", {"stroke": underlineColor, "stroke-width": "3px", "stroke-opacity": "0.9", "stroke-dasharray": "2,2"});
        }
    }
}'''

    content = content.replace(old_exports, new_exports)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js')