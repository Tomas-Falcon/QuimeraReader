import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    content = content.replace('@if (Book.HasAudio && _segments.Any())', '@if (Book.HasAudio && _segments != null && _segments.Any())')
    content = content.replace('if (!string.IsNullOrEmpty(_selectedText) && _segments.Any())', 'if (!string.IsNullOrEmpty(_selectedText) && _segments != null && _segments.Any())')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Components/ReaderPlayer.razor')