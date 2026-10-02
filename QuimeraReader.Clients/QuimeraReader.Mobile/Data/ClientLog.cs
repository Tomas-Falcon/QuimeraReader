using System;
namespace QuimeraReader.Mobile.Data
{
    public class ClientLog
    {
        public int Id { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
