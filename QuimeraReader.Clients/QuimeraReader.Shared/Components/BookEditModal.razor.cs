using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Shared.Components;

public partial class BookEditModal : ComponentBase
{
    [Inject] public IBookService BookService { get; set; } = default!;
    [Inject] public Radzen.DialogService DialogService { get; set; } = default!;

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