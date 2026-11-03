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
}
