import sys
import glob

def make_responsive(path):
    try:
        with open(path, 'r', encoding='utf-8') as f:
            content = f.read()

        # Fix flex rows that don't have columns on small screens
        content = content.replace('d-flex flex-wrap gap-2 align-items-center', 'd-flex flex-column flex-sm-row flex-wrap gap-2 align-items-stretch align-items-sm-center w-100')
        content = content.replace('style=\"flex: 0 0 250px;\"', 'style=\"flex: 1 1 250px;\"')
        
        with open(path, 'w', encoding='utf-8') as f:
            f.write(content)
    except Exception as e:
        print(f"Failed {path}: {e}")

files = [
    'QuimeraReader.Clients/QuimeraReader.Shared/Components/Pages/Authors.razor',
    'QuimeraReader.Clients/QuimeraReader.Shared/Components/Pages/Categories.razor'
]

for file in files:
    make_responsive(file)