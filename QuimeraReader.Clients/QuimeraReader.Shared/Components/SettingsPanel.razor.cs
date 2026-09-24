using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Shared.Components;

public partial class SettingsPanel : ComponentBase
{
    [Inject] public IBookService BookService { get; set; } = default!;
    [Inject] public IScanService ScanService { get; set; } = default!;
    [Inject] public ISettingsService SettingsService { get; set; } = default!;
    [Inject] public ToastService ToastService { get; set; } = default!;

    private bool _isLoading = true;
    private bool _isSaving = false;
    private string _message = string.Empty;
    private bool _isSuccess = false;
    private bool _isScanning = false;
    private ScanStatus? _scanStatus;
    private CancellationTokenSource _cts = new();

    private string _libraryRootPath = "/media";
    private string _outgoingLibraryPath = "/app/media/books";
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

    private class OptionItem
    {
        public string Text { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

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

    protected override async Task OnInitializedAsync()
    {
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
        }
        catch (Exception ex)
        {
            ShowMessage("Error cargando ajustes: " + ex.Message, false);
        }
        finally
        {
            _isLoading = false;
        }

        _ = PollStatusAsync();
    }

    private async Task PollStatusAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, _cts.Token);
                _scanStatus = await ScanService.GetScanStatusAsync();
                StateHasChanged();
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