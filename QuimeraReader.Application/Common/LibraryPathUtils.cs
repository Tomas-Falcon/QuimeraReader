using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Common;

public static class LibraryPathUtils
{
    /// <summary>
    /// Carpeta del libro en la biblioteca, donde van su audio y su portada: la del EPUB si está dentro de la biblioteca
    /// (también en modo torrent, donde ahí está el enlace al EPUB); si no, biblioteca/Autor/Título.
    /// Antes se usaba la carpeta de la portada o de la primera pista, y una pista mal colocada (o una portada que era
    /// una URL) arrastraba los audios nuevos a la carpeta de otro libro.
    /// </summary>
    public static async Task<string> GetBookOutputDirAsync(Book book, IAppDbContext db)
    {
        var libraryRoot = await db.SystemSettings.Where(s => s.Key == "LibraryRootPath").Select(s => s.Value).FirstOrDefaultAsync();
        return GetBookOutputDir(book, libraryRoot);
    }

    public static string GetBookOutputDir(Book book, string? libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = DefaultLibraryRoot;
        var root = libraryRoot.Replace("\\", "/").TrimEnd('/') + "/";

        if (!string.IsNullOrEmpty(book.EpubFilePath) && book.EpubFilePath.Replace("\\", "/").StartsWith(root, StringComparison.Ordinal))
        {
            return Path.GetDirectoryName(book.EpubFilePath)!;
        }

        string safeAuthor = SafeFileName(book.Authors?.FirstOrDefault()?.Author?.Name, "Unknown Author");
        string safeTitle = SafeFileName(book.Title, "Unknown Book");
        return Path.Combine(libraryRoot, safeAuthor, safeTitle);
    }

    /// <summary>Extensión para un audio: la del nombre original, la del archivo en disco o .mp3 (antes quedaban archivos sin extensión).</summary>
    public static string AudioExtension(string? originalFileName, string? physicalPath)
    {
        var ext = Path.GetExtension(originalFileName ?? "");
        if (string.IsNullOrEmpty(ext)) ext = Path.GetExtension(physicalPath ?? "");
        return string.IsNullOrEmpty(ext) ? ".mp3" : ext.ToLowerInvariant();
    }

    /// <summary>
    /// Biblioteca a usar si no hay ninguna configurada: /library (el volumen de Docker) si existe. Nunca una carpeta
    /// dentro de /app, que se pierde al recrear el contenedor (así se perdieron los EPUB guardados en /app/media/books).
    /// </summary>
    public static string DefaultLibraryRoot => System.IO.Directory.Exists("/library") ? "/library" : System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library");

    /// <summary>Ruta dentro del contenedor que no está en un volumen (se borra al actualizar la imagen).</summary>
    public static bool IsEphemeralContainerPath(string? path) =>
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && !string.IsNullOrWhiteSpace(path)
        && (path.Replace("\\", "/").TrimEnd('/') == "/app" || path.Replace("\\", "/").StartsWith("/app/", StringComparison.Ordinal));

    /// <summary>
    /// Convierte un texto no confiable (nombre subido por el cliente, título de metadatos) en un único segmento de ruta seguro:
    /// sin directorios, sin separadores de ningún SO y sin "." / "..".
    /// </summary>
    public static string SafeFileName(string? name, string fallback = "archivo")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        // Los separadores se sustituyen (no se recorta al último segmento) para no perder parte de títulos como "AC/DC".
        // Se incluyen '\' y ':' explícitamente porque en Linux no figuran en GetInvalidFileNameChars.
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':' }).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Trim('.').Length == 0) return fallback;
        return cleaned;
    }

    /// <summary>
    /// Devuelve una ruta dentro de <paramref name="directory"/> que no pisa ningún archivo existente ("x.mp3", "x (2).mp3", ...).
    /// </summary>
    public static string GetUniqueFilePath(string directory, string fileName)
    {
        var safeName = SafeFileName(fileName);
        var candidate = Path.Combine(directory, safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);

        for (int i = 2; File.Exists(candidate); i++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({i}){extension}");
        }
        return candidate;
    }
}
