using QuimeraReader.Shared.Services;

namespace QuimeraReader.Desktop.Services;

public class DesktopServerConfigService : IServerConfigService
{
    private const string ServerUrlKey = "ServerUrl";
    private const string ApiKeyKey = "ApiKey";

    public bool NeedsConfiguration => string.IsNullOrWhiteSpace(DesktopPreferences.Get(ServerUrlKey, ""));
    
    public string? ServerUrl 
    { 
        get 
        { 
            var url = DesktopPreferences.Get(ServerUrlKey, ""); 
            return string.IsNullOrWhiteSpace(url) ? null : url; 
        } 
    }

    public void SaveServerUrl(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        DesktopPreferences.Set(ServerUrlKey, url);
    }

    public string? ApiKey
    {
        get
        {
            var key = DesktopPreferences.Get(ApiKeyKey, "");
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
    }

    public void SaveApiKey(string? apiKey)
    {
        DesktopPreferences.Set(ApiKeyKey, apiKey?.Trim() ?? "");
    }
}
