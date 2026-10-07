namespace QuimeraReader.Shared.Interfaces;

public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public interface ITranslationService
{
    Task InitializeAsync();
    Task LoadLanguageAsync(string langCode);
    string this[string key] { get; }
    event System.Action? OnTranslationsLoaded;
    List<LanguageOption> GetAvailableLanguages();
    string CurrentLanguage { get; }
}