using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using QuimeraReader.Shared.Interfaces;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.IO;
using System.Linq;

namespace QuimeraReader.Shared.Services;

public class TranslationService : ITranslationService
{
    private Dictionary<string, string> _translations = new();
    private List<LanguageOption>? _availableLanguages;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<TranslationService> _logger;
    private string _currentLanguage = "es";

    public event Action? OnTranslationsLoaded;

    public string CurrentLanguage => _currentLanguage;

    public TranslationService(IJSRuntime jsRuntime, ILogger<TranslationService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var storedLang = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "preferredLanguage");
            if (!string.IsNullOrWhiteSpace(storedLang))
            {
                _currentLanguage = storedLang;
            }
        }
        catch (Exception ex) 
        { 
            _logger.LogDebug(ex, "[TranslationService] No se pudo leer el idioma de LocalStorage. Usando por defecto: {Lang}", _currentLanguage);
        }
        
        await LoadLanguageInternalAsync(_currentLanguage);
    }

    public async Task LoadLanguageAsync(string langCode)
    {
        _currentLanguage = langCode;
        try 
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "preferredLanguage", langCode);
        }
        catch { }
        
        await LoadLanguageInternalAsync(langCode);
    }

    public List<LanguageOption> GetAvailableLanguages()
    {
        if (_availableLanguages != null) return _availableLanguages;

        var list = new List<LanguageOption>();
        var assembly = typeof(TranslationService).Assembly;
        var prefix = "QuimeraReader.Shared.wwwroot.Translations.";
        var suffix = ".json";

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var code = resource.Substring(prefix.Length, resource.Length - prefix.Length - suffix.Length);
                string name = code.ToUpperInvariant();

                try
                {
                    using var stream = assembly.GetManifestResourceStream(resource);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("Language_Name", out var langProp) && !string.IsNullOrWhiteSpace(langProp.GetString()))
                        {
                            name = langProp.GetString()!;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TranslationService] Error leyendo Language_Name para {Code}", code);
                }

                list.Add(new LanguageOption { Code = code, Name = name });
            }
        }

        _availableLanguages = list.OrderBy(l => l.Name).ToList();
        return _availableLanguages;
    }

    private async Task LoadLanguageInternalAsync(string langCode)
    {
        try
        {
            // The cleanest, most bulletproof way to read static assets across Web and MAUI without HttpClient issues
            var assembly = typeof(TranslationService).Assembly;
            var resourceName = $"QuimeraReader.Shared.wwwroot.Translations.{langCode}.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                
                if (data != null)
                {
                    _translations = data;
                    OnTranslationsLoaded?.Invoke();
                    return;
                }
            }
            else
            {
                _logger.LogWarning("[TranslationService] Recurso incrustado no encontrado: {ResourceName}. Intentando por red...", resourceName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TranslationService] Error cargando idioma desde recursos incrustados: {Lang}", langCode);
        }
    }

    public string this[string key] => _translations.TryGetValue(key, out var value) ? value : key;
}