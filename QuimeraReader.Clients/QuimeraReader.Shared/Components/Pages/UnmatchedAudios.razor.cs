using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Models;
using Radzen;

namespace QuimeraReader.Shared.Components.Pages;

public partial class UnmatchedAudios : ComponentBase
{
    [Inject] public NavigationManager Navigation { get; set; } = default!;

    private bool _isLoading = false;
    private bool _isAutoMatching = false;
    private bool? _filterMatched = null;

    private List<dynamic> _filterOptions = new List<dynamic>
    {
        new { Text = "Todos los Audios", Value = (bool?)null },
        new { Text = "Asignados", Value = (bool?)true },
        new { Text = "Huérfanos", Value = (bool?)false }
    };

    private List<ManagedAudioDto> _audios = new();
    private List<ManagedAudioDto> _filteredAudios = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadAudios();
    }

    private async Task LoadAudios()
    {
        _isLoading = true;
        try
        {
            var result = await Http.GetFromJsonAsync<List<ManagedAudioDto>>("api/books/all-audios");
            if (result != null)
            {
                _audios = result;
                ApplyFilter();
            }
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

    private void OnFilterChanged()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_filterMatched.HasValue)
        {
            _filteredAudios = _audios.Where(a => a.IsMatched == _filterMatched.Value).ToList();
        }
        else
        {
            _filteredAudios = _audios.ToList();
        }
    }

    private void NavigateToBook(int? bookId)
    {
        if (bookId.HasValue)
        {
            Navigation.NavigateTo($"/book/{bookId.Value}");
        }
    }

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

    private async Task DeleteAudio(ManagedAudioDto audio)
    {
        var confirm = await DialogService.Confirm($"Â¿Eliminar definitivamente '{audio.FileName}'?", "Eliminar Audio", new ConfirmOptions { OkButtonText = "Sí", CancelButtonText = "No" });
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

    private async Task OpenMatchDialog(ManagedAudioDto audio)
    {
        var bookIdRes = await DialogService.OpenAsync<BookSelectionModal>(TranslationService["BookSelection_Title"], null, new Radzen.DialogOptions() { Width = "500px", Height = "600px" });
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

