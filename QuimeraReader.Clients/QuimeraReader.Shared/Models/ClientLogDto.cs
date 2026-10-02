using System;
namespace QuimeraReader.Shared.Models
{
    public class ClientLogDto
    {
        public int Id { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
