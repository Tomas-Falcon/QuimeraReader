using QuimeraReader.Shared.Services;

namespace QuimeraReader.Desktop.Services;

public class DesktopServerConfigService : IServerConfigService
{
    private const string ServerUrlKey = "ServerUrl";

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
}
