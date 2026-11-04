namespace QuimeraReader.Domain.Entities;

/// <summary>
/// Metadatos ya resueltos de un EPUB (incluidas las correcciones a mano), por hash de archivo. Sobrevive a un
/// vaciado de la biblioteca: al volver a importar el mismo EPUB se usan estos datos en vez de volver a
/// consultar Google Books, OpenLibrary, etc.
/// </summary>
public class BookMetadataCache
{
    public int Id { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>Autores en orden, separados por salto de línea.</summary>
    public string? Authors { get; set; }
    public string? Description { get; set; }
    public string? Isbn { get; set; }
    public double? AverageRating { get; set; }
    public int? TotalPages { get; set; }
    /// <summary>Categorías separadas por salto de línea.</summary>
    public string? Categories { get; set; }
    public string? SeriesName { get; set; }
    public double? SeriesVolume { get; set; }
    /// <summary>Portada local que tenía el libro (en la biblioteca), si sigue existiendo se reutiliza.</summary>
    public string? CoverImagePath { get; set; }
    public bool IsMetadataComplete { get; set; }
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
}
