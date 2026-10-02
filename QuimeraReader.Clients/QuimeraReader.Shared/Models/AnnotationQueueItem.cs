namespace QuimeraReader.Shared.Models;
public class AnnotationQueueItem
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString();
    public int BookId { get; set; }
    public string CfiRange { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
}
