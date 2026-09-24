import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Fix JS percentage reporting
    old_report = '''    function reportPercentage(location, dotNetRef, book) {
        if (!location || !location.start || !location.start.cfi) return;
        var percentage = -1;
        try {
            if (book.locations && book.locations.length > 0) {
                percentage = book.locations.percentageFromCfi(location.start.cfi);
            }
        } catch(e) { }
        
        if (percentage === null || percentage === undefined || percentage < 0) percentage = -1;
        if (dotNetRef) {'''
        
    new_report = '''    function reportPercentage(location, dotNetRef, book) {
        if (!location || !location.start || !location.start.cfi) return;
        var percentage = -1;
        try {
            if (book.locations && book.locations.length > 0) {
                var p = book.locations.percentageFromCfi(location.start.cfi);
                if (typeof p === 'number' && !isNaN(p) && isFinite(p)) {
                    percentage = p;
                }
            }
        } catch(e) { }
        
        if (percentage < 0) percentage = -1;
        
        console.log(""Reporting location: "", location.start.cfi, "" percentage: "", percentage);
        if (dotNetRef) {'''

    content = content.replace(old_report, new_report)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/wwwroot/epubInterop.js')