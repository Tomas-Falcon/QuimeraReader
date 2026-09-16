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
    
    // Virtual URLs para el frontend (generadas por MediaController)
    public string CoverUrl => $"/api/media/books/{Id}/cover";
    public string EpubUrl => $"/api/media/books/{Id}/epub";
    public string AudioUrl => $"/api/media/books/{Id}/audio";
    public string PackageUrl => $"/api/media/books/{Id}/package?format=audiobook";
}

public record PaginatedResult<T>
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public T[] Data { get; init; } = [];
}

public record SystemSetting
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
