using System;

namespace QuimeraReader.Shared.Models;

public class UnmatchedAudioTrack
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}
