using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

public class MobileServerConfigService : IServerConfigService
{
    private const string ServerUrlKey = "ServerUrl";

    public bool NeedsConfiguration => string.IsNullOrWhiteSpace(Preferences.Default.Get(ServerUrlKey, ""));
    
    public string? ServerUrl { get { var url = Preferences.Default.Get(ServerUrlKey, ""); return string.IsNullOrWhiteSpace(url) ? null : url; } }

    public void SaveServerUrl(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        Preferences.Default.Set(ServerUrlKey, url);
    }
}