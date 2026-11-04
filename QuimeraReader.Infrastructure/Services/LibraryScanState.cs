namespace QuimeraReader.Infrastructure.Services;

public class LibraryScanState
{
    public bool IsScanning { get; set; }
    public int TotalFilesFound { get; set; }
    public int FilesProcessed { get; set; }
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>Mientras está a true el escáner no empieza ni continúa (p. ej. durante el vaciado de la biblioteca).</summary>
    public volatile bool Paused;
}
