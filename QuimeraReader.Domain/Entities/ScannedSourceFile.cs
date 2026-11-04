namespace QuimeraReader.Domain.Entities;

/// <summary>
/// Cada EPUB que el escáner ya ha procesado, incluidos los duplicados que se saltó y los que fallaron,
/// para no volver a leerlos (ni a calcular su hash) en cada escaneo.
/// </summary>
public class ScannedSourceFile
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public int? BookId { get; set; }
    /// <summary>Imported, Duplicate, Relinked o Error.</summary>
    public string Outcome { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}
