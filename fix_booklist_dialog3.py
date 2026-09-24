import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('@inject DialogService DialogService', '@inject Radzen.DialogService DialogServiceInstance')
content = content.replace('await DialogService.Confirm(', 'await DialogServiceInstance.Confirm(')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)