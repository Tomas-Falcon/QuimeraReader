namespace QuimeraReader.Shared.Models;

public record Book
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Isbn { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string EpubFilePath { get; init; } = string.Empty;
    public string AudioFilePath { get; init; } = string.Empty;
    public string CoverImagePath { get; init; } = string.Empty;
    
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
