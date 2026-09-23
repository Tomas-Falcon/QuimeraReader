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
    private IEnumerable<string> _readingStatuses = new[] { "Unread", "Reading", "Read", "NextToRead" };

    protected override async Task OnInitializedAsync()
    {
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