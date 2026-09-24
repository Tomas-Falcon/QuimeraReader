using System;

namespace QuimeraReader.Domain.Entities;

public class UnmatchedAudioTrack
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
