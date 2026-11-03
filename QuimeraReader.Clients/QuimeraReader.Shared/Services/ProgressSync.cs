using System.Net.Http.Json;

namespace QuimeraReader.Shared.Services;

/// <summary>
/// Subida del progreso de lectura guardado offline. Compartido por los sincronizadores de MAUI, Desktop y Web.
/// </summary>
public static class ProgressSync
{
    /// <summary>
    /// Solo se sube el progreso local si se leyó después que el del servidor. El porcentaje no cuenta:
    /// retroceder o releer desde el principio en otro dispositivo es tan válido como avanzar.
    /// </summary>
    public static bool ShouldPushLocalProgress(Book local, Book server)
    {
        return local.LastReadAt.HasValue &&
               (!server.LastReadAt.HasValue || local.LastReadAt.Value > server.LastReadAt.Value);
    }

    /// <summary>
    /// Envía el progreso local con la hora real de lectura (ReadAt): el servidor lo descarta si ya tiene uno más reciente.
    /// Se envía directamente por HTTP, sin pasar por BookService, para no volver a fechar la copia local como "leída ahora".
    /// Devuelve true si el servidor lo aplicó.
    /// </summary>
    public static async Task<bool> PushLocalProgressAsync(HttpClient httpClient, Book local)
    {
        var request = new UpdatePositionRequest
        {
            CurrentEpubCfi = local.CurrentEpubCfi,
            CurrentAudioPosition = local.CurrentAudioPosition,
            CurrentAudioTrackNumber = local.CurrentAudioTrackNumber,
            PercentageCompleted = local.PercentageCompleted,
            // Las fechas locales se guardan en UTC (SQLite/IndexedDB las devuelven sin Kind)
            ReadAt = local.LastReadAt.HasValue ? DateTime.SpecifyKind(local.LastReadAt.Value, DateTimeKind.Utc) : null
        };

        using var response = await httpClient.PostAsJsonAsync($"api/Books/{local.Id}/positions", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PushResult>();
        return result?.Applied ?? true;
    }

    private sealed class PushResult
    {
        public bool Applied { get; set; }
    }
}
