import urllib.request
try:
    resp = urllib.request.urlopen('http://vm202.panther-carat.ts.net:5000/api/Books/categories')
    print(resp.read().decode('utf-8'))
except Exception as e:
    print('ERROR:', e)
