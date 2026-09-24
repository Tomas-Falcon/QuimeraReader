import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Services/BookService.cs'
with open(path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

for i, line in enumerate(lines):
    if "foreach(var cid in categoryIds) url +=" in line:
        lines.insert(i+2, '            if (!string.IsNullOrWhiteSpace(readingStatus)) url += $"&readingStatus={readingStatus}";\n')
        break

with open(path, 'w', encoding='utf-8') as f:
    f.writelines(lines)