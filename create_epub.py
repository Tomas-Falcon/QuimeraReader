import zipfile

def create_epub(filename, title):
    with zipfile.ZipFile(filename, 'w') as epub:
        epub.writestr('mimetype', 'application/epub+zip')
        
        container = '''<?xml version="1.0"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
    <rootfiles>
        <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
    </rootfiles>
</container>'''
        epub.writestr('META-INF/container.xml', container)
        
        content = f'''<?xml version="1.0"?>
<package version="2.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
    <dc:title>{title}</dc:title>
    <dc:language>es</dc:language>
    <dc:identifier id="BookId">urn:uuid:12345</dc:identifier>
    <dc:creator opf:role="aut">Autor de Prueba</dc:creator>
  </metadata>
  <manifest>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
    <item id="index" href="index.html" media-type="application/xhtml+xml"/>
  </manifest>
  <spine toc="ncx">
    <itemref idref="index"/>
  </spine>
</package>'''
        epub.writestr('OEBPS/content.opf', content)
        
        toc = f'''<?xml version="1.0"?>
<ncx version="2005-1" xmlns="http://www.daisy.org/z3986/2005/ncx/">
  <head>
    <meta name="dtb:uid" content="urn:uuid:12345"/>
    <meta name="dtb:depth" content="1"/>
    <meta name="dtb:totalPageCount" content="0"/>
    <meta name="dtb:maxPageNumber" content="0"/>
  </head>
  <docTitle><text>{title}</text></docTitle>
  <navMap>
    <navPoint id="navPoint-1" playOrder="1">
      <navLabel><text>Inicio</text></navLabel>
      <content src="index.html"/>
    </navPoint>
  </navMap>
</ncx>'''
        epub.writestr('OEBPS/toc.ncx', toc)
        
        index = f'''<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>{title}</title></head>
<body><h1>{title}</h1><p>Esto es un libro de prueba.</p></body>
</html>'''
        epub.writestr('OEBPS/index.html', index)

create_epub('libro_test_duplicado.epub', 'Libro de Prueba Duplicados')
print('EPUB creado!')
