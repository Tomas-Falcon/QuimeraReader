namespace QuimeraReader.Shared.Services;

public interface IServerConfigService
{
    bool NeedsConfiguration { get; }
    string? ServerUrl { get; }
    void SaveServerUrl(string url);

    /// <summary>
    /// Clave de acceso a la API (QUIMERA_API_KEY del servidor). En la web es null: allí la sesión va en una cookie HttpOnly.
    /// </summary>
    string? ApiKey { get; }
    void SaveApiKey(string? apiKey);

    /// <summary>
    /// true si los medios (portadas, audio, EPUB) deben pedirse a través de <see cref="MediaProxy"/> en lugar de
    /// cargarse directamente desde el servidor (necesario en MAUI Android con servidores http://).
    /// </summary>
    bool UseNativeMediaProxy { get; }
}
