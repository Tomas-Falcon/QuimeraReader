import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/Layout/NavMenu.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<span class="bi bi-house-door-fill-nav-menu" aria-hidden="true"></span>', '<RadzenIcon Icon="library_books" Style="margin-right: 8px;" />')
content = content.replace('<span class="bi bi-people-fill-nav-menu" aria-hidden="true"></span>', '<RadzenIcon Icon="people" Style="margin-right: 8px;" />')
content = content.replace('<span class="bi bi-tags-fill-nav-menu" aria-hidden="true"></span>', '<RadzenIcon Icon="local_offer" Style="margin-right: 8px;" />')
content = content.replace('<span class="bi bi-check-circle-fill-nav-menu" aria-hidden="true"></span>', '<RadzenIcon Icon="check_circle" Style="margin-right: 8px;" />')
content = content.replace('<span class="bi bi-gear-fill-nav-menu" aria-hidden="true"></span>', '<RadzenIcon Icon="settings" Style="margin-right: 8px;" />')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)