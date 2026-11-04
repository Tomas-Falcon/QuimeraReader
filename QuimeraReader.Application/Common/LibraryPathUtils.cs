using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Interfaces;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Application.Common;

public static class LibraryPathUtils
{
    public static async Task<string> GetBookOutputDirAsync(Book book, IAppDbContext db)
    {
        if (!string.IsNullOrEmpty(book.CoverImagePath)) return Path.GetDirectoryName(book.CoverImagePath)!;
        if (book.AudioTracks != null && book.AudioTracks.Any()) return Path.GetDirectoryName(book.AudioTracks.First().FilePath)!;
        
        string safeAuthor = string.Join("_", (book.Authors?.FirstOrDefault()?.Author?.Name ?? "Unknown Author").Split(Path.GetInvalidFileNameChars()));
        string safeTitle = string.Join("_", book.Title.Split(Path.GetInvalidFileNameChars()));
        
        var settingsDict = await db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = QuimeraReader.Application.Common.LibraryPathUtils.DefaultLibraryRoot;
        
        return Path.Combine(libraryRoot, safeAuthor, safeTitle);
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
