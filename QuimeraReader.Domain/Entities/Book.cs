namespace QuimeraReader.Domain.Entities;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ProcessingStatus { get; set; } // mapped to frontend's processing_status (e.g., "SYNCED")
    
    public string? Isbn { get; set; }
    public string? Description { get; set; }
    public double? AverageRating { get; set; }
    public int? TotalPages { get; set; }

    // Local File Paths
    public string EpubFilePath { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; } // Tracks original path for LeaveInPlace mode
    public string? CoverImagePath { get; set; }
    
    // Reading Progress
    public DateTime? LastReadAt { get; set; }
    public string? ReadingStatus { get; set; } // Unread, Reading, Read, NextToRead
    public string? CurrentEpubCfi { get; set; }
    public string? EpubLocationsCache { get; set; } // JSON cache of epub.js locations
    public double? CurrentAudioPosition { get; set; }
    public double? PercentageCompleted { get; set; }

    // Series/Sagas
    public int? SeriesId { get; set; }
    public Series? Series { get; set; }
    public double? SeriesVolume { get; set; } // Puede ser 1.5, etc.

    // Metadata Tracking
    public bool IsMetadataComplete { get; set; } = false;
    
    // Sincronizacin
    public bool IsReadInHardcover { get; set; }

    // Relations
    public ICollection<BookAuthor> Authors { get; set; } = new List<BookAuthor>();
    public ICollection<BookCategory> Categories { get; set; } = new List<BookCategory>();
    public ICollection<BookAudioTrack> AudioTracks { get; set; } = new List<BookAudioTrack>();
    public SyncMap? SyncMap { get; set; }
}