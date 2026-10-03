using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

using QuimeraReader.Shared.Models;
using Radzen;

namespace QuimeraReader.Shared.Components.Pages;

public partial class UnmatchedAudios : ComponentBase
{
        private bool _isLoading = false;
    private bool _isAutoMatching = false;

    private async Task AutoMatchAudios()
    {
        _isAutoMatching = true;
        try
        {
            var response = await Http.PostAsync("api/books/unmatched-audios/auto-match", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                ToastService.ShowSuccess($"Proceso iniciado: {result}");
                await LoadAudios();
            }
            else
            {
                ToastService.ShowError("Error al iniciar el proceso automático.");
            }
        }
        catch (Exception ex)
        {
            ToastService.ShowError("Error: " + ex.Message);
        }
        finally
        {
            _isAutoMatching = false;
        }
    }
    private List<UnmatchedAudioTrack> _audios = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadAudios();
    }

    private async Task LoadAudios()
    {
        _isLoading = true;
        try
        {
            var result = await Http.GetFromJsonAsync<List<UnmatchedAudioTrack>>("api/books/unmatched-audios");
            if (result != null)
                _audios = result;
        }
        catch (Exception ex)
        {
            ToastService.ShowError("Error al cargar audios: " + ex.Message);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task DeleteAudio(UnmatchedAudioTrack audio)
    {
        var confirm = await DialogService.Confirm($"¿Eliminar definitivamente '{audio.OriginalFileName}'?", "Eliminar Audio", new ConfirmOptions { OkButtonText = "Sí", CancelButtonText = "No" });
        if (confirm == true)
        {
            try
            {
                var response = await Http.DeleteAsync($"api/books/unmatched-audios/{audio.Id}");
                if (response.IsSuccessStatusCode)
                {
                    ToastService.ShowSuccess("Audio eliminado");
                    await LoadAudios();
                }
                else
                {
                    ToastService.ShowError("No se pudo eliminar el audio.");
                }
            }
            catch (Exception ex)
            {
                ToastService.ShowError("Error: " + ex.Message);
            }
        }
    }

    private async Task OpenMatchDialog(UnmatchedAudioTrack audio)
    {
        var bookIdRes = await DialogService.OpenAsync<BookSelectionModal>("Seleccionar Libro", null, new Radzen.DialogOptions() { Width = "500px", Height = "600px" });
        if (bookIdRes is int bookId)
        {
            try
            {
                var response = await Http.PostAsync($"api/books/unmatched-audios/{audio.Id}/match/{bookId}", null);
                if (response.IsSuccessStatusCode)
                {
                    ToastService.ShowSuccess("Audio asociado correctamente");
                    await LoadAudios();
                }
                else
                {
                    ToastService.ShowError("No se pudo asociar el audio.");
                }
            }
            catch (Exception ex)
            {
                ToastService.ShowError("Error: " + ex.Message);
            }
        }
    }
}




