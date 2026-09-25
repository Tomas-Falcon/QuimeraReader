namespace QuimeraReader.Domain.Entities
{
    public class BookAnnotation
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public Book? Book { get; set; }
        public string CfiRange { get; set; } = string.Empty;
        public string SelectedText { get; set; } = string.Empty;
        public string? Note { get; set; }
        public string? ColorHex { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}