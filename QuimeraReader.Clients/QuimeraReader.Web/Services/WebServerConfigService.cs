using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Web.Services;

public class WebServerConfigService : IServerConfigService
{
    private readonly NavigationManager _navManager;

    public WebServerConfigService(NavigationManager navManager)
    {
        _navManager = navManager;
    }

    public bool NeedsConfiguration => false; // En web nunca lo necesita
    public string? ServerUrl => _navManager.BaseUri;

    public void SaveServerUrl(string url)
    {
        // No-op en web
    }
}