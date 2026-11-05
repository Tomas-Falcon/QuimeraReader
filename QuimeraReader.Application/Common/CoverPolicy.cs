using System;
using System.Collections.Generic;
using System.Linq;

namespace QuimeraReader.Application.Common;

/// <summary>
/// Qué portada se queda un libro y qué metadatos online se aceptan. Antes la portada de internet siempre ganaba a la
/// del EPUB, aunque fuera una miniatura de 128 px o de otro libro.
/// </summary>
public static class CoverPolicy
{
    // La portada online tiene que ser claramente más grande para desplazar a la del EPUB
    private const double OnlineMustBeTallerBy = 1.2;

    /// <summary>
    /// La del EPUB salvo que no haya, o que la de internet sea claramente más grande.
    /// Si no se puede leer el tamaño de la del EPUB, se respeta igualmente (es la que eligió quien publicó el libro).
    /// </summary>
    public static byte[]? Choose(byte[]? epubCover, byte[]? onlineCover)
    {
        if (epubCover == null || epubCover.Length == 0) return onlineCover;
        if (onlineCover == null || onlineCover.Length == 0) return epubCover;

        var epubSize = GetImageSize(epubCover);
        var onlineSize = GetImageSize(onlineCover);
        if (epubSize == null || onlineSize == null) return epubCover;
        return onlineSize.Value.Height > epubSize.Value.Height * OnlineMustBeTallerBy ? onlineCover : epubCover;
    }

    /// <summary>true si conviene reemplazar la portada actual por la nueva (no hay actual o la nueva es claramente más grande).</summary>
    public static bool ShouldReplace(byte[]? current, byte[]? candidate) =>
        candidate != null && candidate.Length > 0 && ReferenceEquals(Choose(current, candidate), candidate);

    /// <summary>
    /// Google Books devuelve por defecto una miniatura de 128 px con una "esquina doblada" dibujada.
    /// Sin edge=curl y con fife=w800 sirve la misma portada a 800 px de ancho.
    /// </summary>
    public static string? GoogleCoverUrl(string? thumbnail)
    {
        if (string.IsNullOrWhiteSpace(thumbnail)) return null;
        var url = thumbnail.Replace("http:", "https:").Replace("&edge=curl", "");
        return url.Contains("fife=") ? url : url + "&fife=w800";
    }

    /// <summary>
    /// ¿Algún autor del resultado coincide con el que ya conocemos? Tiene que aparecer el apellido (la última palabra
    /// significativa), sin tildes ni mayúsculas: compartir solo el nombre ("James Q. Glenn" con "James S. A. Corey")
    /// no basta. Sin autor conocido o sin autores en el resultado no se puede descartar.
    /// </summary>
    public static bool AuthorsMatch(string? knownAuthor, IEnumerable<string>? candidateAuthors)
    {
        var surname = Surname(knownAuthor);
        if (surname == null) return true;
        var candidates = candidateAuthors?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (candidates == null || candidates.Count == 0) return true;
        return candidates.Any(c => Words(c).Contains(surname));
    }

    // "Corey, James S. A." también vale: se busca el apellido entre todas las palabras del candidato
    private static string? Surname(string? name)
    {
        var folded = TextNormalizer.Fold(name);
        var parts = folded.Split(',', 2);
        var words = SplitWords(parts.Length == 2 ? parts[0] : folded);
        return parts.Length == 2 ? words.FirstOrDefault() : words.LastOrDefault();
    }

    private static HashSet<string> Words(string? name) => SplitWords(TextNormalizer.Fold(name)).ToHashSet();

    private static List<string> SplitWords(string folded) =>
        folded.Split(new[] { ' ', '.', ',', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .ToList();

    /// <summary>Ancho y alto leyendo solo la cabecera (JPEG, PNG, GIF, WebP). null si no se reconoce.</summary>
    public static (int Width, int Height)? GetImageSize(byte[] data)
    {
        if (data == null || data.Length < 24) return null;

        // PNG: IHDR justo después de la firma
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return (BigEndian32(data, 16), BigEndian32(data, 20));

        // GIF
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return (data[6] | data[7] << 8, data[8] | data[9] << 8);

        // WebP
        if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data.Length >= 30 &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            string chunk = System.Text.Encoding.ASCII.GetString(data, 12, 4);
            if (chunk == "VP8 ") return ((data[26] | data[27] << 8) & 0x3FFF, (data[28] | data[29] << 8) & 0x3FFF);
            if (chunk == "VP8L") return (1 + (data[21] | (data[22] & 0x3F) << 8), 1 + ((data[22] >> 6) | data[23] << 2 | (data[24] & 0x0F) << 10));
            if (chunk == "VP8X") return (1 + (data[24] | data[25] << 8 | data[26] << 16), 1 + (data[27] | data[28] << 8 | data[29] << 16));
            return null;
        }

        // JPEG: recorrer los segmentos hasta un SOFn
        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                byte marker = data[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
                int length = data[i + 2] << 8 | data[i + 3];
                bool isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof) return (data[i + 7] << 8 | data[i + 8], data[i + 5] << 8 | data[i + 6]);
                i += 2 + length;
            }
        }
        return null;
    }

    private static int BigEndian32(byte[] d, int o) => d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3];
}
