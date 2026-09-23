namespace QuimeraReader.Domain.Entities;

public class BookAudioTrack
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    
    public double? DurationSeconds { get; set; }
}
