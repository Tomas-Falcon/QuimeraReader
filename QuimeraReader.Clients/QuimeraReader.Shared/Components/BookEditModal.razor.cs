using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;
using System.Net.Http.Json;
using System.Net.Http;

namespace QuimeraReader.Shared.Components;

public partial class BookEditModal : ComponentBase
{
    [Inject] public IBookService BookService { get; set; } = default!;
    [Inject] public Radzen.DialogService DialogService { get; set; } = default!;
    [Inject] public HttpClient Http { get; set; } = default!;
    [Inject] public QuimeraReader.Shared.Interfaces.ITranslationService TranslationService { get; set; } = default!;

    private bool _isSearchingCovers = false;
    private List<string> _suggestedCovers = new();

    private async Task SearchCoversAsync()
    {
        _isSearchingCovers = true;
        _suggestedCovers.Clear();
        StateHasChanged();

        try
        {
            var response = await Http.GetAsync($"api/books/{Book.Id}/cover/search");
            if (response.IsSuccessStatusCode)
            {
                var covers = await response.Content.ReadFromJsonAsync<List<string>>();
                if (covers != null)
                {
                    _suggestedCovers = covers;
                }
            }
        }
        catch { }
        finally
        {
            _isSearchingCovers = false;
            StateHasChanged();
        }
    }

    private void SelectCover(string url)
    {
        _coverUrl = url;
    }

    [Parameter]
    public Book Book { get; set; } = default!;

    private UpdateMetadataRequest _request = new();
    private string _coverUrl = string.Empty;

    private IEnumerable<Category> _availableCategories = Array.Empty<Category>();
        public class StatusOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
    private IEnumerable<StatusOption> _readingStatuses = new List<StatusOption>();

    protected override async Task OnInitializedAsync()
    {
                _readingStatuses = new List<StatusOption>
        {
            new StatusOption { Value = "Unread", Label = TranslationService["Status_Unread"] },
            new StatusOption { Value = "Reading", Label = TranslationService["Status_CurrentlyReading"] },
            new StatusOption { Value = "Read", Label = TranslationService["Status_Read"] },
            new StatusOption { Value = "NextToRead", Label = TranslationService["Status_NextToRead"] }
        };
        
        _availableCategories = await BookService.GetCategoriesAsync();
        
        if (Book != null)
        {
            _request.Title = Book.Title;
            _request.ReadingStatus = string.IsNullOrEmpty(Book.ReadingStatus) ? "Unread" : Book.ReadingStatus;
            _request.Categories = Book.Categories?.ToList() ?? new List<string>();
        }
    }

    private async Task SaveAsync()
    {
        await BookService.UpdateMetadataAsync(Book.Id, _request);
        if (!string.IsNullOrWhiteSpace(_coverUrl))
        {
            await BookService.UpdateCoverAsync(Book.Id, _coverUrl);
        }
        DialogService.Close(true);
    }
}