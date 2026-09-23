namespace QuimeraReader.Shared.Services;

public interface IServerConfigService
{
    bool NeedsConfiguration { get; }
    string? ServerUrl { get; }
    void SaveServerUrl(string url);
}