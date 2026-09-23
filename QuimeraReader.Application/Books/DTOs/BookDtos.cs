using System;
using System.Collections.Generic;

namespace QuimeraReader.Application.Books.DTOs;

public record BookDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Isbn { get; init; }
    public string? Description { get; init; }
    public double? AverageRating { get; init; }
    public string? ProcessingStatus { get; init; }
    
    public List<string> Authors { get; init; } = new();
    public List<string> Categories { get; init; } = new();
    public string? Series { get; init; }
    public string? Universe { get; init; }
    
    public bool HasCover { get; init; }
    public bool HasEpub { get; init; }
    public bool HasAudio { get; init; }
    public bool IsAligned { get; init; }
    
    public DateTime? LastReadAt { get; init; }
    public string? CurrentEpubCfi { get; init; }
    public double? CurrentAudioPosition { get; init; }
    public double? PercentageCompleted { get; init; }
}

public record PaginatedListDto<T>
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<T> Data { get; init; } = new();
}

public record AuthorDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? ProfileImageUrl { get; init; }
    public int BookCount { get; init; }
    public string? SampleCoverUrl { get; init; }
}

public record CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsUserGenerated { get; init; }
    public int BookCount { get; init; }
    public string? SampleCoverUrl { get; init; }
}

