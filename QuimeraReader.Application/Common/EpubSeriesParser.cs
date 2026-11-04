using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuimeraReader.Application.Common;

/// <summary>Una etiqueta &lt;meta&gt; del OPF de un EPUB (EPUB2 usa name/content; EPUB3 property/refines y el texto).</summary>
public record EpubMeta(string? Name, string? Content, string? Id = null, string? Refines = null, string? Property = null);

/// <summary>Saga (serie) declarada en los metadatos de un EPUB.</summary>
public record EpubSeriesInfo(string Name, double? Volume);

/// <summary>
/// Extrae la saga de los metadatos del EPUB:
/// - EPUB2 / Calibre: &lt;meta name="calibre:series" content="Matabot"/&gt; + calibre:series_index.
/// - EPUB3: &lt;meta property="belongs-to-collection" id="c1"&gt;Matabot&lt;/meta&gt; refinado con
///   collection-type (series/set) y group-position.
/// </summary>
public static class EpubSeriesParser
{
    public static EpubSeriesInfo? Parse(IEnumerable<EpubMeta> metas)
    {
        var list = metas.ToList();

        // EPUB3: preferir colecciones de tipo "series" frente a "set" o sin tipo
        var collections = list
            .Where(m => string.Equals(m.Property, "belongs-to-collection", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m =>
            {
                var refinements = list.Where(r => !string.IsNullOrEmpty(m.Id) && r.Refines?.TrimStart('#') == m.Id).ToList();
                var type = refinements.FirstOrDefault(r => string.Equals(r.Property, "collection-type", StringComparison.OrdinalIgnoreCase))?.Content;
                var position = refinements.FirstOrDefault(r => string.Equals(r.Property, "group-position", StringComparison.OrdinalIgnoreCase))?.Content;
                return (Name: m.Content!, Type: type?.Trim().ToLowerInvariant(), Position: position);
            })
            .OrderBy(c => c.Type == "series" ? 0 : c.Type == null ? 1 : 2)
            .ToList();
        if (collections.Count > 0)
        {
            var best = collections[0];
            return Create(best.Name, best.Position);
        }

        // EPUB2 / Calibre
        var calibreName = list.FirstOrDefault(m => string.Equals(m.Name, "calibre:series", StringComparison.OrdinalIgnoreCase))?.Content;
        if (!string.IsNullOrWhiteSpace(calibreName))
        {
            var index = list.FirstOrDefault(m => string.Equals(m.Name, "calibre:series_index", StringComparison.OrdinalIgnoreCase))?.Content;
            return Create(calibreName, index);
        }

        return null;
    }

    private static EpubSeriesInfo? Create(string rawName, string? rawVolume)
    {
        var name = NormalizeName(rawName);
        if (name.Length == 0) return null;

        double? volume = null;
        if (!string.IsNullOrWhiteSpace(rawVolume) &&
            double.TryParse(rawVolume.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0 && parsed < 100000)
        {
            volume = parsed;
        }
        return new EpubSeriesInfo(name, volume);
    }

    /// <summary>Espacios colapsados y recortados: "  Matabot " y "Matabot" son la misma saga.</summary>
    public static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : Regex.Replace(name.Trim(), @"\s+", " ");
}
