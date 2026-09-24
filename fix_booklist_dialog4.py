import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('@inject Radzen.DialogService DialogServiceInstance', '@inject Radzen.DialogService _dialogService')
content = content.replace('await DialogServiceInstance.Confirm(', 'await _dialogService.Confirm(')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)