using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Revisa qué libros apuntan a archivos (EPUB o portada) que ya no existen y, cuando es posible,
/// los reencuentra por nombre de archivo en las carpetas de la biblioteca y de entrada.
/// </summary>
public static class LibraryFileAudit
{
    public class FolderGroup
    {
        public string Folder { get; set; } = "";
        public int Count { get; set; }
        public int MinBookId { get; set; }
        public int MaxBookId { get; set; }
        public List<string> Samples { get; set; } = new();
    }

    public class Report
    {
        public int TotalBooks { get; set; }
        public int EpubOk { get; set; }
        public int EpubMissing { get; set; }
        public int EpubFoundElsewhere { get; set; }
        public int EpubAmbiguous { get; set; }
        public int CoverOk { get; set; }
        public int CoverMissing { get; set; }
        public int CoverFoundElsewhere { get; set; }
        public int Repaired { get; set; }
        public bool Applied { get; set; }
        public List<string> SearchedFolders { get; set; } = new();
        public List<FolderGroup> MissingEpubFolders { get; set; } = new();
    }

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    /// <summary>Índice nombre de archivo (sin distinguir mayúsculas) → rutas completas.</summary>
    public static Dictionary<string, List<string>> BuildFileIndex(IEnumerable<string> folders, CancellationToken cancellationToken = default, Action<int>? onProgress = null)
    {
        int seen = 0;
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var folder in folders)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++seen % 5000 == 0) onProgress?.Invoke(seen);
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".epub" && !ImageExtensions.Contains(ext)) continue;
                var name = Path.GetFileName(file);
                if (!index.TryGetValue(name, out var list)) index[name] = list = new List<string>();
                if (!list.Contains(file, StringComparer.Ordinal)) list.Add(file);
            }
        }
        onProgress?.Invoke(seen);
        return index;
    }

    /// <summary>Agrupa por los primeros segmentos de la ruta (p. ej. "/media/torrents/Libros").</summary>
    public static string GroupKey(string path, int segments = 3)
    {
        var parts = path.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dirParts = parts.Take(Math.Max(0, parts.Length - 1)).Take(segments);
        var prefix = path.StartsWith('/') || path.StartsWith('\\') ? "/" : "";
        return prefix + string.Join("/", dirParts);
    }

    /// <summary>
    /// Revisa todos los libros. Con <paramref name="apply"/> guarda las rutas reencontradas (solo coincidencias únicas).
    /// </summary>
    public static async Task<Report> RunAsync(AppDbContext db, string? libraryRoot, IEnumerable<string> searchFolders, bool apply,
        CancellationToken cancellationToken = default, Action<int>? onFilesIndexed = null)
    {
        var report = new Report { Applied = apply };
        var folders = searchFolders
            .Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f))
            .Select(f => Path.GetFullPath(f).TrimEnd('/', '\\'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // Quitar carpetas contenidas en otra para no recorrerlas dos veces
        folders = folders.Where(f => !folders.Any(o => o != f && f.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.Ordinal))).ToList();
        report.SearchedFolders = folders;

        var index = BuildFileIndex(folders, cancellationToken, onFilesIndexed);

        var books = await db.Books.AsNoTracking()
            .Select(b => new { b.Id, b.EpubFilePath, b.CoverImagePath })
            .ToListAsync(cancellationToken);
        report.TotalBooks = books.Count;

        var groups = new Dictionary<string, FolderGroup>();
        var epubFixes = new Dictionary<int, string>();
        var coverFixes = new Dictionary<int, string>();

        foreach (var book in books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? epubDir = null;

            if (!string.IsNullOrEmpty(book.EpubFilePath))
            {
                var resolved = LibraryPathResolver.Resolve(book.EpubFilePath, libraryRoot);
                if (resolved != null)
                {
                    report.EpubOk++;
                    epubDir = Path.GetDirectoryName(resolved);
                }
                else
                {
                    report.EpubMissing++;
                    var key = GroupKey(book.EpubFilePath);
                    if (!groups.TryGetValue(key, out var group))
                    {
                        groups[key] = group = new FolderGroup { Folder = key, MinBookId = book.Id, MaxBookId = book.Id };
                    }
                    group.Count++;
                    group.MinBookId = Math.Min(group.MinBookId, book.Id);
                    group.MaxBookId = Math.Max(group.MaxBookId, book.Id);
                    if (group.Samples.Count < 5) group.Samples.Add(book.EpubFilePath);

                    if (index.TryGetValue(Path.GetFileName(book.EpubFilePath.Replace("\\", "/")), out var candidates))
                    {
                        if (candidates.Count == 1)
                        {
                            report.EpubFoundElsewhere++;
                            epubFixes[book.Id] = candidates[0];
                            epubDir = Path.GetDirectoryName(candidates[0]);
                        }
                        else report.EpubAmbiguous++;
                    }
                }
            }

            if (!string.IsNullOrEmpty(book.CoverImagePath))
            {
                if (LibraryPathResolver.Resolve(book.CoverImagePath, libraryRoot) != null) report.CoverOk++;
                else
                {
                    report.CoverMissing++;
                    // La portada se guarda junto al EPUB: buscarla primero en su carpeta, luego por nombre único
                    var coverName = Path.GetFileName(book.CoverImagePath.Replace("\\", "/"));
                    var sameDir = epubDir != null ? Path.Combine(epubDir, coverName) : null;
                    if (sameDir != null && File.Exists(sameDir)) coverFixes[book.Id] = sameDir;
                    else if (index.TryGetValue(coverName, out var candidates) && candidates.Count == 1) coverFixes[book.Id] = candidates[0];
                    if (coverFixes.ContainsKey(book.Id)) report.CoverFoundElsewhere++;
                }
            }
        }

        report.MissingEpubFolders = groups.Values.OrderByDescending(g => g.Count).ToList();

        if (apply && (epubFixes.Count > 0 || coverFixes.Count > 0))
        {
            foreach (var chunk in epubFixes.Keys.Union(coverFixes.Keys).Chunk(500))
            {
                var tracked = await db.Books.Where(b => chunk.Contains(b.Id)).ToListAsync(cancellationToken);
                foreach (var book in tracked)
                {
                    if (epubFixes.TryGetValue(book.Id, out var epub)) book.EpubFilePath = epub;
                    if (coverFixes.TryGetValue(book.Id, out var cover)) book.CoverImagePath = cover;
                    report.Repaired++;
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return report;
    }
}
