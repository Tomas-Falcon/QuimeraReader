namespace QuimeraReader.Domain.Entities;

public class Series
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public int? UniverseId { get; set; }
    public Universe? Universe { get; set; }
    
    public ICollection<Book> Books { get; set; } = new List<Book>();
}
