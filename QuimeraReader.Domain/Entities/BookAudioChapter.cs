namespace QuimeraReader.Domain.Entities;

public class BookAudioChapter
{
    public int Id { get; set; }
    public int BookAudioTrackId { get; set; }
    public BookAudioTrack? AudioTrack { get; set; }
    
    public string Title { get; set; } = string.Empty;
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
}
