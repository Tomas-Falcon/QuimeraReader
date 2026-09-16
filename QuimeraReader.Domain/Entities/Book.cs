namespace QuimeraReader.Domain.Entities;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ProcessingStatus { get; set; } // mapped to frontend's processing_status (e.g., "SYNCED")
    
    // Local File Paths
    public string EpubFilePath { get; set; } = string.Empty;
    public string? AudioFilePath { get; set; }
    public string? CoverImagePath { get; set; }
    
    // Reading Progress
    public DateTime? LastReadAt { get; set; }

    // Series/Sagas
    public int? SeriesId { get; set; }
    public Series? Series { get; set; }
    public double? SeriesVolume { get; set; } // Puede ser 1.5, etc.
    
    // Relations
    public ICollection<BookAuthor> Authors { get; set; } = new List<BookAuthor>();
    public ICollection<BookCategory> Categories { get; set; } = new List<BookCategory>();
    public SyncMap? SyncMap { get; set; }
}
