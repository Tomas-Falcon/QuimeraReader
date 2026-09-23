namespace QuimeraReader.Domain.Entities;

public class SyncMap
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;
    
    // Guardaremos el JSON serializado de la sincronización generada por Whisper
    public string SyncMapJson { get; set; } = string.Empty; 
}
