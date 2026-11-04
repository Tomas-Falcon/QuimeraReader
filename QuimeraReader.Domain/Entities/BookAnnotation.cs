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

        /// <summary>null = marca normal (subrayado o nota); <see cref="ListenKind"/> = "Seguir escuchando aquí".</summary>
        public string? Kind { get; set; }
        // Para "Seguir escuchando aquí": dónde arranca el audio (principio de la frase marcada, un poco antes)
        public double? AudioPosition { get; set; }
        public int? AudioTrackNumber { get; set; }

        public const string ListenKind = "listen";
    }
}