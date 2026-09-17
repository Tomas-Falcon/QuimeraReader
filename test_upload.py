import requests
import time

url_upload = 'http://localhost:5005/api/Books/upload'
url_books = 'http://localhost:5005/api/Books'

def get_book_count():
    r = requests.get(url_books)
    if r.status_code == 200:
        return r.json()['total']
    return -1

# Wait for API to start
for _ in range(10):
    try:
        requests.get(url_books)
        break
    except:
        time.sleep(1)

initial_count = get_book_count()
print(f'Initial book count: {initial_count}')

# Upload 1
print('Uploading test.epub for the first time...')
with open('test.epub', 'rb') as f:
    r = requests.post(url_upload, files={'file': ('test.epub', f, 'application/epub+zip')})
    print(f'Upload 1 status: {r.status_code}')

count_after_1 = get_book_count()
print(f'Book count after 1st upload: {count_after_1}')

# Upload 2
print('Uploading test.epub for the second time...')
with open('test.epub', 'rb') as f:
    r = requests.post(url_upload, files={'file': ('test.epub', f, 'application/epub+zip')})
    print(f'Upload 2 status: {r.status_code}')

count_after_2 = get_book_count()
print(f'Book count after 2nd upload: {count_after_2}')

if count_after_1 == count_after_2 and count_after_1 > initial_count:
    print('SUCCESS: Duplicate prevented!')
else:
    print('FAILED: Duplicates were created or upload failed.')
