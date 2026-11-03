using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using QuimeraReader.Application.Common;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Lee solo el OPF (metadatos) de un EPUB sin cargar el libro completo: para recorrer decenas de miles de
/// archivos rápido (relleno de sagas) sin el coste de EpubReader.ReadBookAsync.
/// </summary>
public static class EpubOpfReader
{
    public static EpubSeriesInfo? ReadSeries(string epubPath)
    {
        using var zip = ZipFile.OpenRead(epubPath);
        return EpubSeriesParser.Parse(ReadMetas(zip));
    }

    public static IReadOnlyList<EpubMeta> ReadMetas(ZipArchive zip)
    {
        var opfPath = FindOpfPath(zip);
        if (opfPath == null) return new List<EpubMeta>();

        var entry = zip.GetEntry(opfPath) ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(opfPath, System.StringComparison.OrdinalIgnoreCase));
        if (entry == null) return new List<EpubMeta>();

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "meta")
            .Select(e => new EpubMeta(
                Name: (string?)e.Attribute("name"),
                // EPUB2 guarda el valor en content; EPUB3 en el texto del elemento
                Content: (string?)e.Attribute("content") ?? (string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim()),
                Id: (string?)e.Attribute("id"),
                Refines: (string?)e.Attribute("refines"),
                Property: (string?)e.Attribute("property")))
            .ToList();
    }

    private static string? FindOpfPath(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml");
        if (container != null)
        {
            using var stream = container.Open();
            var doc = XDocument.Load(stream);
            var path = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }
        // EPUB sin container.xml válido: el primer .opf que aparezca
        return zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", System.StringComparison.OrdinalIgnoreCase))?.FullName;
    }
}
