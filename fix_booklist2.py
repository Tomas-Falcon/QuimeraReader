import sys

path = 'QuimeraReader.Clients/QuimeraReader.Shared/Components/BookList.razor'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace("try\n        {\n            try\n        {", "try\n        {")
content = content.replace("}\n        }\n        catch { }", "}\n        catch { }")

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)