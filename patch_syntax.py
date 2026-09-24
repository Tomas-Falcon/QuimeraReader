import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Find the endpoints I accidentally placed outside
    bad_block = '''}

    [HttpPut("{id}/offline")]'''
    
    good_block = '''
    [HttpPut("{id}/offline")]'''

    if bad_block in content:
        content = content.replace(bad_block, good_block)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.API/Controllers/BooksController.cs')