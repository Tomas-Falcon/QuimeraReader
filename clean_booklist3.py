import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('@if ((_virtualizeComponent == null) && !_isLoading)', '@if (false)')
content = content.replace('if (string.IsNullOrWhiteSpace(QueryParam) && (_virtualizeComponent == null) && !_isLoading)', 'if (string.IsNullOrWhiteSpace(QueryParam) && (_virtualizeComponent == null))')
content = content.replace('if (string.IsNullOrWhiteSpace(QueryParam) && _virtualizeComponent == null)', 'if (string.IsNullOrWhiteSpace(QueryParam) && _virtualizeComponent == null)')
content = re.sub(r'public async Task LoadMoreRequested\(\)\n\s*\{\s*\}', '', content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)