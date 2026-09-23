using QuimeraReader.Shared.Services;

namespace QuimeraReader.Mobile.Services;

public class MobileServerConfigService : IServerConfigService
{
    private const string ServerUrlKey = "ServerUrl";

    public bool NeedsConfiguration => string.IsNullOrWhiteSpace(Preferences.Default.Get<string>(ServerUrlKey, null));
    
    public string? ServerUrl => Preferences.Default.Get<string>(ServerUrlKey, null);

    public void SaveServerUrl(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        Preferences.Default.Set(ServerUrlKey, url);
    }
}