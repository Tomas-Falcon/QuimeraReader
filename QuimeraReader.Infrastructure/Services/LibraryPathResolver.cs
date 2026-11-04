using System.IO;
using System.Linq;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Encuentra en disco un archivo guardado con otra ruta base (p. ej. BD creada en Windows y servidor en Docker):
/// si la ruta tal cual no existe, prueba sus últimos 4, 3 y 2 segmentos bajo la carpeta de la biblioteca.
/// Misma lógica que usa MediaController para servir archivos.
/// </summary>
public static class LibraryPathResolver
{
    public static string? Resolve(string? originalPath, string? libraryRoot)
    {
        if (string.IsNullOrEmpty(originalPath)) return null;
        if (File.Exists(originalPath)) return originalPath;
        if (string.IsNullOrWhiteSpace(libraryRoot)) return null;

        var parts = originalPath.Replace("\\", "/").Split('/', System.StringSplitOptions.RemoveEmptyEntries);
        for (int depth = 4; depth >= 2; depth--)
        {
            if (parts.Length < depth) continue;
            var candidate = Path.Combine(new[] { libraryRoot }.Concat(parts.Skip(parts.Length - depth)).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
