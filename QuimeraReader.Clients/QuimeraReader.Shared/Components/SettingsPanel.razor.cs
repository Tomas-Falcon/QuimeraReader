using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Shared.Components;

public partial class SettingsPanel : ComponentBase
{
    [Inject] public IBookService BookService { get; set; } = default!;
    [Inject] public IScanService ScanService { get; set; } = default!;
    [Inject] public ISettingsService SettingsService { get; set; } = default!;
    [Inject] public ToastService ToastService { get; set; } = default!;
    [Inject] public IServerConfigService ServerConfig { get; set; } = default!;
    [Inject] public NavigationManager NavManager { get; set; } = default!;
    [Inject] public HttpClient HttpClient { get; set; } = default!;
    [Inject] public ILogger<SettingsPanel> Logger { get; set; } = default!;

    private bool _isLoading = true;
    private bool _isSaving = false;
    private bool _isTestingClientConnection = false;
    private bool _isSendingLogs = false;
    private string _message = string.Empty;
    private string _clientServerUrl = string.Empty;
    private string _clientApiKey = string.Empty;
    private bool _isSuccess = false;
    private bool _isScanning = false;
    private ScanStatus? _scanStatus;
    private CancellationTokenSource _cts = new();

    private string _libraryRootPath = "/media";
    // /library es el volumen de Docker; el valor anterior (/app/media/books) estaba dentro del contenedor y se perdía al actualizar
    private string _outgoingLibraryPath = "/library";
    private bool _serverSettingsLoaded;
    private string _ingestionMode = "Move";
    private string _openAiKey = string.Empty;
    private string _googleBooksKey = string.Empty;
    private string _hardcoverKey = string.Empty;
    private bool _hardcoverSyncEnabled = false;
    private string _whisperModelPath = "ggml-base.bin";
    private string _whisperMode = "LocalServer";
    private string _openAiEndpoint = "https://api.openai.com/v1/";
    private bool _isDownloadingWhisper = false;
    private bool _isWhisperDownloaded = false;
    private bool _showAdvancedServerSettings = false;

    private bool IsMobileClient => ServerConfig?.GetType().Name.Contains("Mobile", StringComparison.OrdinalIgnoreCase) == true;
    private bool ShowServerAdminTabs => !IsMobileClient || _showAdvancedServerSettings;

    private class OptionItem
    {
        public string Text { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private List<OptionItem> _languageOptions = new()
    {
        new OptionItem { Text = "Español (ES)", Value = "es" },
        new OptionItem { Text = "English (EN)", Value = "en" },
        new OptionItem { Text = "Deutsch (DE)", Value = "de" },
        new OptionItem { Text = "Français (FR)", Value = "fr" },
        new OptionItem { Text = "Italiano (IT)", Value = "it" },
        new OptionItem { Text = "Português (PT)", Value = "pt" }
    };

    private List<OptionItem> _ingestionModes = new()
    {
        new OptionItem { Text = "Mover y Organizar (Ahorra Espacio)", Value = "Move" },
        new OptionItem { Text = "Dejar en lugar original (Para Torrents - Crea Enlaces/Symlinks)", Value = "LeaveInPlace" }
    };

    private List<OptionItem> _whisperModes = new()
    {
        new OptionItem { Text = "Plug & Play: Ejecutar Whisper localmente en este Servidor", Value = "LocalServer" },
        new OptionItem { Text = "API Externa: Conectar a OpenAI o red local (Ollama / LLMStudio)", Value = "ExternalApi" }
    };

    private void OnForceOfflineChanged(bool value)
    {
        NetworkStateService.SetForceOffline(value);
        ToastService.ShowSuccess(value ? "Modo sin conexión activado." : "Modo en línea restaurado.");
        StateHasChanged();
    }

    private async Task OnLanguageChanged(object value)
    {
        if (value is string lang && !string.IsNullOrEmpty(lang))
        {
            await TranslationService.LoadLanguageAsync(lang);
            StateHasChanged();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (ServerConfig?.ServerUrl != null)
        {
            _clientServerUrl = ServerConfig.ServerUrl;
            _clientApiKey = ServerConfig.ApiKey ?? string.Empty;
        }

        // En móvil, mostrar las preferencias del cliente de inmediato sin esperar al servidor
        if (IsMobileClient)
        {
            _isLoading = false;
            StateHasChanged();
        }

        try
        {
            var settings = await SettingsService.GetSettingsAsync();
            if (settings.TryGetValue("IncomingScanFolder", out var incoming)) _libraryRootPath = incoming;
            if (settings.TryGetValue("LibraryRootPath", out var outgoing)) _outgoingLibraryPath = outgoing;
            if (settings.TryGetValue("IngestionMode", out var mode)) _ingestionMode = mode;
            if (settings.TryGetValue("OpenAIApiKey", out var openai)) _openAiKey = openai;
            if (settings.TryGetValue("GoogleBooksApiKey", out var gbooks)) _googleBooksKey = gbooks;
            if (settings.TryGetValue("HardcoverApiKey", out var hardcover)) _hardcoverKey = hardcover;
            if (settings.TryGetValue("HardcoverSyncEnabled", out var hSync)) _hardcoverSyncEnabled = bool.TryParse(hSync, out var b) && b;
            if (settings.TryGetValue("WhisperModelPath", out var whisper)) _whisperModelPath = whisper;
            if (settings.TryGetValue("WhisperMode", out var wMode)) _whisperMode = wMode;
            if (settings.TryGetValue("OpenAIEndpoint", out var wEndpoint)) _openAiEndpoint = wEndpoint;
            _serverSettingsLoaded = true;

            if (ShowServerAdminTabs)
            {
                _isWhisperDownloaded = await SettingsService.CheckWhisperModelStatusAsync();
            }
        }
        catch (Exception ex)
        {
            if (!IsMobileClient)
            {
                ShowMessage("Error cargando ajustes: " + ex.Message, false);
            }
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }

        _ = PollStatusAsync();
    }

    private async Task SendDiagnosticLogAsync()
    {
        _isSendingLogs = true;
        StateHasChanged();

        try
        {
            Logger.LogInformation("Reporte de diagnóstico manual enviado desde el cliente móvil. ServerUrl={Url}", _clientServerUrl);
            var payload = new List<ClientLogDto>
            {
                new ClientLogDto
                {
                    Level = "Information",
                    Message = $"[DiagnosticReport] Ping manual desde Ajustes del Cliente Android (ServerUrl={_clientServerUrl}, ForceOffline={NetworkStateService.IsForceOffline})",
                    CreatedAt = DateTime.UtcNow
                }
            };
            var response = await HttpClient.PostAsJsonAsync("api/Logs", payload);
            if (response.IsSuccessStatusCode)
            {
                ToastService.ShowSuccess("Logs enviados correctamente al servidor (/api/Logs).");
            }
            else
            {
                ToastService.ShowError($"El servidor respondió con código {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al enviar reporte de diagnóstico manual.");
            ToastService.ShowError("No se pudo conectar al servidor para enviar los logs; se enviarán en segundo plano.");
        }
        finally
        {
            _isSendingLogs = false;
            StateHasChanged();
        }
    }

    private async Task PollStatusAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, _cts.Token);
                if (ShowServerAdminTabs && !NetworkStateService.IsOffline)
                {
                    _scanStatus = await ScanService.GetScanStatusAsync();
                    StateHasChanged();
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task SaveSettingsAsync()
    {
        // Guardar sin haber leído los ajustes del servidor pisaría las rutas y claves reales con los valores por defecto
        if (!_serverSettingsLoaded)
        {
            ShowMessage("No se pudieron leer los ajustes del servidor; no se guarda nada para no sobrescribirlos. Recarga la página.", false);
            return;
        }

        _isSaving = true;
        _message = string.Empty;
        StateHasChanged();

        try
        {
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "IncomingScanFolder", Value = _libraryRootPath });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "LibraryRootPath", Value = _outgoingLibraryPath });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "IngestionMode", Value = _ingestionMode });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "OpenAIApiKey", Value = _openAiKey });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "GoogleBooksApiKey", Value = _googleBooksKey });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "HardcoverApiKey", Value = _hardcoverKey });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "HardcoverSyncEnabled", Value = _hardcoverSyncEnabled.ToString() });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "WhisperModelPath", Value = _whisperModelPath });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "WhisperMode", Value = _whisperMode });
            await SettingsService.SaveSettingAsync(new SystemSetting { Key = "OpenAIEndpoint", Value = _openAiEndpoint });
            
            ShowMessage("Ajustes guardados correctamente.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al guardar: " + ex.Message, false);
        }
        finally
        {
            _isSaving = false;
            StateHasChanged();
        }
    }

    private async Task SaveClientConfig()
    {
        if (string.IsNullOrWhiteSpace(_clientServerUrl)) return;

        var rawUrl = _clientServerUrl.Trim();
        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = "http://" + rawUrl;
        }
        if (!rawUrl.EndsWith("/")) rawUrl += "/";

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var testUri))
        {
            ToastService.ShowError(TranslationService["Settings_InvalidUrl"]);
            return;
        }

        _isTestingClientConnection = true;
        StateHasChanged();

        bool canConnect = false;
        try
        {
            using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var pingUri = new Uri(testUri, "api/health"); // público: no requiere la API key
            using var response = await testClient.GetAsync(pingUri);
            if (response.IsSuccessStatusCode)
            {
                canConnect = true;
            }
        }
        catch
        {
            canConnect = false;
        }
        finally
        {
            _isTestingClientConnection = false;
        }

        ServerConfig.SaveServerUrl(rawUrl);
        ServerConfig.SaveApiKey(_clientApiKey);

        if (canConnect)
        {
            ToastService.ShowSuccess(TranslationService["Settings_ConnectionSuccess"]);
        }
        else
        {
            ToastService.ShowError(TranslationService["Settings_ConnectionFailed"]);
        }

        await Task.Delay(1500);
        NavManager.NavigateTo(NavManager.Uri, forceLoad: true);
    }

    private async Task DownloadWhisperModelAsync()
    {
        _isDownloadingWhisper = true;
        _message = string.Empty;
        StateHasChanged();
        
        try
        {
            await SettingsService.DownloadWhisperModelAsync();
            _isWhisperDownloaded = true;
            ShowMessage("Modelo descargado y sincronización reanudada.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al descargar: " + ex.Message, false);
        }
        finally
        {
            _isDownloadingWhisper = false;
            StateHasChanged();
        }
    }

    private async Task TriggerScanAsync()
    {
        _isScanning = true;
        _message = string.Empty;
        StateHasChanged();

        try
        {
            var response = await ScanService.TriggerScanAsync(new { FolderPath = _libraryRootPath });
            ShowMessage("El escáner se ha iniciado en segundo plano.", true);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error al iniciar escaneo: {ex.Message}", false);
        }
        finally
        {
            _isScanning = false;
            StateHasChanged();
        }
    }

    private async Task CancelScanAsync()
    {
        try
        {
            await ScanService.CancelScanAsync();
            ShowMessage("Escaneo cancelado.", true);
            if (_scanStatus != null) _scanStatus.IsScanning = false;
            _isScanning = false;
        }
        catch (Exception ex)
        {
            ShowMessage("Error cancelando el escaneo: " + ex.Message, false);
        }
    }

    private async Task RescanMetadataAsync()
    {
        try
        {
            await ScanService.RescanMetadataAsync();
            ShowMessage("Se ha iniciado la búsqueda de metadatos en segundo plano.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al buscar metadatos: " + ex.Message, false);
        }
    }

    private async Task MergeDuplicatesAsync()
    {
        try
        {
            await ScanService.MergeDuplicatesAsync();
            ShowMessage("Mantenimiento de duplicados completado exitosamente.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al fusionar duplicados: " + ex.Message, false);
        }
    }

    private bool _isRestoring;
    private bool _dedupRunning;
    private bool _dedupAnalyzed;
    private string? _dedupStatus;

    private async Task RestoreBackupAsync(Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs e)
    {
        _isRestoring = true;
        try
        {
            using var content = new MultipartFormDataContent();
            // Un ZIP completo puede pesar varios GB
            var stream = e.File.OpenReadStream(maxAllowedSize: 20L * 1024 * 1024 * 1024);
            content.Add(new StreamContent(stream), "file", e.File.Name);
            // El cliente general corta a los 25 s: subir y restaurar un ZIP grande lleva más
            using var uploadClient = new HttpClient { BaseAddress = HttpClient.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
            if (!string.IsNullOrEmpty(ServerConfig.ApiKey)) uploadClient.DefaultRequestHeaders.Add("X-Api-Key", ServerConfig.ApiKey);
            var response = await uploadClient.PostAsync("api/maintenance/backup/restore", content);
            var report = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (!response.IsSuccessStatusCode)
            {
                ShowMessage(report.TryGetProperty("error", out var error) ? error.GetString() ?? "Error al restaurar." : "Error al restaurar.", false);
                return;
            }

            int Get(string name) => report.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : 0;
            int Count(string name) => report.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array ? v.GetArrayLength() : 0;
            int found = Get("matchedByHash") + Get("matchedByIsbn") + Get("matchedByTitle");
            ShowMessage($"Copia restaurada: {found} libros encontrados, {Get("imported")} importados, {Get("annotationsAdded")} notas y {Get("audioTracksAdded")} pistas añadidas, {Count("notFound")} no encontrados.", Count("errors") == 0);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al restaurar: " + ex.Message, false);
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private async Task RunDuplicatesAsync(bool apply)
    {
        _dedupRunning = true;
        _dedupStatus = apply ? "Uniendo libros repetidos..." : "Analizando...";
        try
        {
            await HttpClient.PostAsync($"api/maintenance/duplicates/run?apply={apply.ToString().ToLowerInvariant()}", null);
            System.Text.Json.JsonElement status;
            do
            {
                await Task.Delay(2000, _cts.Token);
                status = await HttpClient.GetFromJsonAsync<System.Text.Json.JsonElement>("api/maintenance/duplicates", _cts.Token);
            } while (status.GetProperty("running").GetBoolean());

            if (status.TryGetProperty("error", out var error) && error.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                _dedupStatus = "Error: " + error.GetString();
                return;
            }

            var r = status.GetProperty("result");
            int Get(string name) => r.GetProperty(name).GetInt32();
            int same = Get("hashDuplicates"), missing = Get("missingMergedIntoCopy");
            int kept = r.GetProperty("missingKeptWithUserData").GetArrayLength();
            _dedupStatus = apply
                ? $"Hecho: {Get("deleted")} libros unidos en su copia ({Get("transferredUserData")} con datos tuyos). Se guardó una copia de la base de datos antes."
                : $"{same} copias del mismo EPUB y {missing} libros sin archivo con copia ({Get("transferredUserData")} con datos tuyos que se conservarán). {Get("missingOrphans")} sin archivo ni copia, {kept} sin copia pero con datos tuyos (se mantienen). Hashes calculados: {Get("booksWithHash")} de {Get("totalBooks")}.";
            _dedupAnalyzed = !apply;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _dedupStatus = "Error: " + ex.Message;
        }
        finally
        {
            _dedupRunning = false;
            StateHasChanged();
        }
    }

    private void ShowMessage(string message, bool isSuccess)
    {
        if (isSuccess)
        {
            ToastService.ShowSuccess(message);
        }
        else
        {
            ToastService.ShowError(message);
        }
    }

    private async Task RestartContainerAsync()
    {
        try
        {
            await SettingsService.RestartServerAsync();
            ShowMessage("Se ha enviado la señal de reinicio.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al reiniciar: " + ex.Message, false);
        }
    }

    private async Task UpdateContainerAsync()
    {
        try
        {
            await SettingsService.UpdateContainerAsync();
            ShowMessage("Se ha enviado la señal de actualización y reinicio.", true);
        }
        catch (Exception ex)
        {
            ShowMessage("Error al actualizar: " + ex.Message, false);
        }
    }
}
