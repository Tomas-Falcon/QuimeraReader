namespace QuimeraReader.Domain.Entities;

public class Universe
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    public ICollection<Series> Series { get; set; } = new List<Series>();
}
