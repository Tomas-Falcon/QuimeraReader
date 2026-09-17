namespace QuimeraReader.Domain.Entities;

public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileAs { get; set; } = string.Empty;
    
    public ICollection<BookAuthor> Books { get; set; } = new List<BookAuthor>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int BookCount { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? SampleCoverUrl { get; set; }
}
