namespace QuimeraReader.Shared.Interfaces;

public interface ITranslationService
{
    Task InitializeAsync();
    Task LoadLanguageAsync(string langCode);
    string this[string key] { get; }
}