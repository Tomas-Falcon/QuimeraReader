import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/SettingsPanel.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace row flex boxes with responsive ones
content = content.replace('d-flex justify-content-between align-items-center', 'd-flex flex-column flex-md-row justify-content-between align-items-md-center gap-3')

# In BookDetails as well
path2 = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/Pages/BookDetails.razor'
with open(path2, 'r', encoding='utf-8') as f:
    content2 = f.read()

# Fix layout of BookDetails
content2 = content2.replace('class=\"col-md-3\"', 'class=\"col-12 col-md-4 col-lg-3\"')
content2 = content2.replace('class=\"col-md-9\"', 'class=\"col-12 col-md-8 col-lg-9 mt-4 mt-md-0\"')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)

with open(path2, 'w', encoding='utf-8') as f:
    f.write(content2)