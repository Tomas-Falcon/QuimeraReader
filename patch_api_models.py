import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Add Local paths
    old_props = '''    public string CoverUrl => $"api/media/books/{Id}/cover" + (CoverCacheBuster > 0 ? $"?t={CoverCacheBuster}" : "");
    public string EpubUrl => $"api/media/books/{Id}/file.epub";
    public string AudioUrl => $"api/media/books/{Id}/audio";'''
    new_props = '''    public string? LocalEpubPath { get; set; }
    public string? LocalCoverPath { get; set; }
    public string? LocalAudioPath { get; set; }

    public string CoverUrl => LocalCoverPath ?? ($"api/media/books/{Id}/cover" + (CoverCacheBuster > 0 ? $"?t={CoverCacheBuster}" : ""));
    public string EpubUrl => LocalEpubPath ?? $"api/media/books/{Id}/file.epub";
    public string AudioUrl => LocalAudioPath ?? $"api/media/books/{Id}/audio";'''
    
    content = content.replace(old_props, new_props)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Models/ApiModels.cs')