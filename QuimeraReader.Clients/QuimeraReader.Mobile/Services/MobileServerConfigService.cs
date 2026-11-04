using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

public class MobileServerConfigService : IServerConfigService
{
    public const string ServerUrlKey = "ServerUrl";
    public const string ApiKeyKey = "ApiKey";

    public bool NeedsConfiguration => string.IsNullOrWhiteSpace(Preferences.Default.Get(ServerUrlKey, ""));
    
    public string? ServerUrl { get { var url = Preferences.Default.Get(ServerUrlKey, ""); return string.IsNullOrWhiteSpace(url) ? null : url; } }

    public void SaveServerUrl(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        Preferences.Default.Set(ServerUrlKey, url);
    }

    public string? ApiKey { get { var key = Preferences.Default.Get(ApiKeyKey, ""); return string.IsNullOrWhiteSpace(key) ? null : key; } }

    public void SaveApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) Preferences.Default.Remove(ApiKeyKey);
        else Preferences.Default.Set(ApiKeyKey, apiKey.Trim());
    }

    // Android y Windows sirven la app desde https://0.0.0.1 y bloquean los medios http:// del servidor (mixed content)
    public bool UseNativeMediaProxy => OperatingSystem.IsAndroid() || OperatingSystem.IsWindows();
}
