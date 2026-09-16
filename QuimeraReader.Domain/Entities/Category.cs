using System.Collections.Generic;

namespace QuimeraReader.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsUserGenerated { get; set; }
    
    public ICollection<BookCategory> Books { get; set; } = new List<BookCategory>();
}
