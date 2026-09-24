import sys
import re

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. HandleFileUpload
content = re.sub(r'_currentPage = 1;\n\s*_hasMore = true;\n', '', content)

# 2. Stray _isLoading
content = re.sub(r'else if \(string.IsNullOrWhiteSpace\(QueryParam\) && \(\_virtualizeComponent == null\) && !_isLoading\)', 'else if (string.IsNullOrWhiteSpace(QueryParam) && _virtualizeComponent == null)', content)

# 3. LoadMoreRequested remaining
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading \|\| !_hasMore\) return;\n\s*try\n\s*\{\n\s*await LoadMoreAsync\(\);\n\s*\}\n\s*catch \{ \}\n\s*\}', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading && _hasMore\) return;\n\s*try\n\s*\{\n\s*await LoadMoreAsync\(\);\n\s*\}\n\s*catch \{ \}\n\s*\}', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading && _hasMore\)\n\s*\{\n\s*LoadMoreAsync\(\);\n\s*StateHasChanged\(\);\n\s*\}\n\s*\}', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\n\s*\{\n\s*if \(!_isLoading && _hasMore\)\n\s*\{\n\s*await LoadMoreAsync\(\);\n\s*StateHasChanged\(\);\n\s*\}\n\s*\}', '', content, flags=re.DOTALL)
content = re.sub(r'    \[JSInvokable\]\n    public async Task LoadMoreRequested\(\)\s*\{[^\}]*\}\s*', '', content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)