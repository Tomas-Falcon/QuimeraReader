namespace QuimeraReader.Shared.Services;

/// <summary>Utilidades comunes de los servidores nativos de archivos offline (ILocalMediaServer)</summary>
public static class LocalMediaFile
{
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m4b" or ".m4a" or ".mp4" => "audio/mp4",
        ".ogg" or ".opus" => "audio/ogg",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".epub" => "application/epub+zip",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "audio/mpeg"
    };

    /// <summary>
    /// Devuelve el archivo de <paramref name="directory"/> que nombra la URL, o null. Solo nombres de archivo sueltos:
    /// nada de rutas, para que la URL no permita leer fuera de la carpeta de descargas offline.
    /// </summary>
    public static string? ResolveFile(string directory, string escapedName)
    {
        var name = Uri.UnescapeDataString(escapedName);
        if (name.Length == 0 || name != Path.GetFileName(name) || name.Contains("..")) return null;
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Interpreta una cabecera Range de un solo tramo ("bytes=a-b", "bytes=a-" o "bytes=-n").
    /// Devuelve null si no hay cabecera válida (se responde 200 con el archivo entero). Todo tramo se limita a
    /// <paramref name="maxChunk"/> bytes (un 206 más corto de lo pedido es válido): el reproductor pide el resto después.
    /// </summary>
    public static ByteRange? ParseRange(string? header, long length, long maxChunk)
    {
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = header.Substring(6).Trim();
        if (spec.Contains(',')) return null;
        var dash = spec.IndexOf('-');
        if (dash < 0) return null;

        var startText = spec.Substring(0, dash).Trim();
        var endText = spec.Substring(dash + 1).Trim();
        long start, end;
        if (startText.Length == 0)
        {
            if (!long.TryParse(endText, out var suffix) || suffix <= 0) return null;
            if (length == 0) return new ByteRange(0, 0, Satisfiable: false);
            start = Math.Max(0, length - suffix);
            end = length - 1;
        }
        else
        {
            if (!long.TryParse(startText, out start) || start < 0) return null;
            if (start >= length) return new ByteRange(start, start, Satisfiable: false);
            end = length - 1;
            if (endText.Length > 0)
            {
                if (!long.TryParse(endText, out var requestedEnd) || requestedEnd < start) return null;
                end = Math.Min(end, requestedEnd);
            }
        }
        end = Math.Min(end, start + maxChunk - 1);
        return new ByteRange(start, end, Satisfiable: true);
    }
}

public record ByteRange(long Start, long End, bool Satisfiable)
{
    public long Length => End - Start + 1;
    public string ContentRange(long totalLength) => Satisfiable ? $"bytes {Start}-{End}/{totalLength}" : $"bytes */{totalLength}";
}
