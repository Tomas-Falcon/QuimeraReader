import requests

url_books = 'http://vm202.panther-carat.ts.net:5000/api/Books'
r = requests.get(url_books)
if r.status_code == 200:
    data = r.json()
    books = data.get('data', [])
    if books:
        for b in books[:3]:
            cover_url = f"http://vm202.panther-carat.ts.net:5000/api/media/books/{b['id']}/cover"
            cr = requests.get(cover_url)
            print(f"Book {b['id']} - Title: {b.get('title')} - Cover Status: {cr.status_code}")
    else:
        print("No books found")
else:
    print(f"Failed to get books: {r.status_code}")
