using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace QuimeraReader.Tests.Common;

/// <summary>Genera un EPUB 2 mínimo válido (con portada opcional) para probar el escáner y las portadas.</summary>
public static class TestEpub
{
    /// <summary>JPEG falso pero con cabecera real y tamaño suficiente para pasar por imagen.</summary>
    public static byte[] FakeJpeg(byte fill = 0x42) =>
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(Enumerable.Repeat(fill, 2048)).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();

    public static void Write(string path, string title, string author, byte[]? cover = null)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open(), new UTF8Encoding(false))) w.Write("application/epub+zip");

        Add(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        var coverMeta = cover != null ? """<meta name="cover" content="cover-img"/>""" : "";
        var coverItem = cover != null ? """<item id="cover-img" href="cover.jpg" media-type="image/jpeg"/>""" : "";
        Add(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:title>{title}</dc:title>
                <dc:creator opf:role="aut">{author}</dc:creator>
                <dc:identifier id="id">urn:uuid:{System.Guid.NewGuid()}</dc:identifier>
                <dc:language>es</dc:language>
                {coverMeta}
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                <item id="c1" href="c1.xhtml" media-type="application/xhtml+xml"/>
                {coverItem}
              </manifest>
              <spine toc="ncx"><itemref idref="c1"/></spine>
            </package>
            """);

        Add(zip, "OEBPS/toc.ncx", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head><meta name="dtb:uid" content="x"/></head>
              <docTitle><text>{title}</text></docTitle>
              <navMap><navPoint id="n1" playOrder="1"><navLabel><text>Uno</text></navLabel><content src="c1.xhtml"/></navPoint></navMap>
            </ncx>
            """);

        Add(zip, "OEBPS/c1.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>{title}</title></head><body><p>Hola</p></body></html>
            """);

        if (cover != null)
        {
            var entry = zip.CreateEntry("OEBPS/cover.jpg", CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(cover);
        }
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content.Trim());
    }
}
