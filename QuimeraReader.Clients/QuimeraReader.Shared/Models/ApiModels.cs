namespace QuimeraReader.Shared.Models;

public record Book
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Isbn { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double? AverageRating { get; init; }
    
    public List<string> Authors { get; init; } = new();
    public List<string> Categories { get; init; } = new();
    public string? Series { get; init; }
    public string? Universe { get; init; }
    
    public bool HasCover { get; init; }
    public bool HasEpub { get; init; }
    public bool HasAudio { get; init; }
    public bool IsAligned { get; init; }
    
    public DateTime? LastReadAt { get; set; }
    public string? CurrentEpubCfi { get; set; }
    public double? CurrentAudioPosition { get; set; }
    public double? PercentageCompleted { get; set; }
    public string? ReadingStatus { get; set; }
    public string? EpubLocationsCache { get; set; }
    public int? TotalPages { get; set; }
    
    // Virtual URLs para el frontend (generadas por MediaController)
    public string CoverUrl => $"api/media/books/{Id}/cover";
    public string EpubUrl => $"api/media/books/{Id}/file.epub";
    public string AudioUrl => $"api/media/books/{Id}/audio";
    public string PackageUrl => $"api/media/books/{Id}/package?format=audiobook";
}

public class UpdatePositionRequest
{
    public string? CurrentEpubCfi { get; set; }
    public double? CurrentAudioPosition { get; set; }
    public double? PercentageCompleted { get; set; }
    public string? ReadingStatus { get; set; }
    public string? EpubLocationsCache { get; set; }
    public int? TotalPages { get; set; }
}

public record PaginatedResult<T>
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public T[] Data { get; init; } = [];
    public T[] Items => Data;
}

public record SystemSetting
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public record Category
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public int BookCount { get; init; }
    public string? SampleCoverUrl { get; init; }
}

public record Author
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ProfileImageUrl { get; init; }
    public int BookCount { get; init; }
    public string? SampleCoverUrl { get; init; }
}

public record ScanStatus
{
    public bool IsScanning { get; set; }
    public int TotalFilesFound { get; set; }
    public int FilesProcessed { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
}

public class BulkStatusUpdateRequest
{
    public List<int> BookIds { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}

public class UpdateMetadataRequest
{
    public string Title { get; set; } = string.Empty;
    public string? ReadingStatus { get; set; }
    public List<string> Categories { get; set; } = new();
}

public class UpdateCoverRequest
{
    public string ImageUrl { get; set; } = string.Empty;
}

public class CoverSearchResult
{
    public string ImageUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
