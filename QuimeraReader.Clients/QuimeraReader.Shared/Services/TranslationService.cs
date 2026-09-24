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

namespace QuimeraReader.Shared.Services;

public class TranslationService : ITranslationService
{
    private Dictionary<string, string> _translations = new();
    private readonly HttpClient _localHttp;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<TranslationService> _logger;
    private string _currentLanguage = "es";

    public event Action? OnTranslationsLoaded;

    public TranslationService(NavigationManager navManager, IJSRuntime jsRuntime, ILogger<TranslationService> logger)
    {
        _localHttp = new HttpClient { BaseAddress = new Uri(navManager.BaseUri) };
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

    private async Task LoadLanguageInternalAsync(string langCode)
    {
        try
        {
            var url = "_content/QuimeraReader.Shared/Translations/$langCode.json";
            Dictionary<string, string>? data = null;
            
            try 
            {
                // Fallback for MAUI: use JS fetch if HttpClient fails for local assets
                var jsonString = await _jsRuntime.InvokeAsync<string>("eval", "fetch('$url').then(r => r.json()).then(j => JSON.stringify(j))");
                data = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
            }
            catch
            {
                // Fallback to traditional HttpClient (works perfectly on Web)
                data = await _localHttp.GetFromJsonAsync<Dictionary<string, string>>(url);
            }

            if (data != null)
            {
                _translations = data;
                OnTranslationsLoaded?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TranslationService] Error cargando el archivo de idioma: {Lang}", langCode);
        }
    }

    public string this[string key] => _translations.TryGetValue(key, out var value) ? value : key;
}