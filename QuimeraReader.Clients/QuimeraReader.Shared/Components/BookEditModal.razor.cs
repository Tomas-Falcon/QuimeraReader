using Microsoft.AspNetCore.Components;
using QuimeraReader.Shared.Models;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Shared.Components;

public partial class BookEditModal : ComponentBase
{
    [Inject] public IBookService BookService { get; set; } = default!;
    [Inject] public Radzen.DialogService DialogService { get; set; } = default!;
    [Inject] public ToastService ToastService { get; set; } = default!;
    [Inject] public QuimeraReader.Shared.Interfaces.ITranslationService TranslationService { get; set; } = default!;

    private const int CoverTabIndex = 1;

    private bool _isSearchingCovers = false;
    private bool _searched = false;
    private bool _isSaving = false;
    private List<CoverSearchResult> _suggestedCovers = new();
    private CoverSearchResult? _selected;
    private bool _fillMissing = true;
    private string _manualUrl = string.Empty;

    // Filtros de búsqueda: por defecto los del libro, editables
    private string _searchTitle = string.Empty;
    private string _searchAuthor = string.Empty;
    private string _searchProvider = string.Empty;

    public class ProviderOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
    private List<ProviderOption> _providers = new();

    private async Task OnTabChanged(int index)
    {
        // Al abrir la pestaña de portada se busca solo, como en Jellyfin
        if (index == CoverTabIndex && !_searched && !_isSearchingCovers) await SearchCoversAsync();
    }

    private async Task SearchCoversAsync()
    {
        _isSearchingCovers = true;
        _suggestedCovers = new();
        _selected = null;
        StateHasChanged();

        try
        {
            _suggestedCovers = await BookService.SearchCoversAsync(Book.Id, _searchTitle, _searchAuthor, _searchProvider);
        }
        catch (Exception ex)
        {
            ToastService.ShowError($"{TranslationService["CoverSearch_Error"]} {ex.Message}");
        }
        finally
        {
            _searched = true;
            _isSearchingCovers = false;
            StateHasChanged();
        }
    }

    private void SelectCover(CoverSearchResult? cover)
    {
        _selected = cover;
        _manualUrl = string.Empty;
    }

    private void OnManualUrlInput(ChangeEventArgs e)
    {
        _manualUrl = e.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_manualUrl)) _selected = null;
    }

    // Con menos votos la media no dice nada (mismo mínimo que el servidor)
    private const int MinRatingVotes = 5;

    /// <summary>La puntuación con más votos entre los resultados del mismo autor, sea cual sea la portada elegida.</summary>
    private CoverSearchResult? BestRated => _suggestedCovers
        .Where(c => c.AuthorMatches && c.Rating > 0 && c.RatingsCount >= MinRatingVotes)
        .OrderByDescending(c => c.RatingsCount)
        .FirstOrDefault();

    private bool CanFillMissing => _selected != null && (_selected.HasExtraMetadata || BestRated != null);

    private string ProviderLabel(string provider) =>
        _providers.FirstOrDefault(p => p.Value == provider)?.Label ?? provider;

    [Parameter]
    public Book Book { get; set; } = default!;

    private UpdateMetadataRequest _request = new();

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

        _providers = new List<ProviderOption>
        {
            new() { Value = "", Label = TranslationService["CoverSearch_All"] },
            new() { Value = CoverSearchResult.EpubProvider, Label = "EPUB" },
            new() { Value = "GoogleBooks", Label = "Google Books" },
            new() { Value = "OpenLibrary", Label = "Open Library" },
            new() { Value = "Hardcover", Label = "Hardcover" },
            new() { Value = "Goodreads", Label = "Goodreads" },
            new() { Value = "StoryGraph", Label = "StoryGraph" }
        };

        _availableCategories = await BookService.GetCategoriesAsync();

        if (Book != null)
        {
            _request.Title = Book.Title;
            _request.ReadingStatus = string.IsNullOrEmpty(Book.ReadingStatus) ? "Unread" : Book.ReadingStatus;
            _request.Categories = Book.Categories?.ToList() ?? new List<string>();
            _searchTitle = Book.Title;
            _searchAuthor = Book.Authors.FirstOrDefault() ?? string.Empty;
        }
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        try
        {
            await BookService.UpdateMetadataAsync(Book.Id, _request);

            UpdateCoverRequest? cover = null;
            if (!string.IsNullOrWhiteSpace(_manualUrl))
            {
                cover = new UpdateCoverRequest { ImageUrl = _manualUrl.Trim() };
            }
            else if (_selected != null)
            {
                bool fill = _fillMissing && CanFillMissing;
                var best = BestRated;
                cover = new UpdateCoverRequest
                {
                    ImageUrl = _selected.ImageUrl,
                    Provider = _selected.Provider,
                    FillMissingMetadata = fill,
                    Synopsis = fill ? _selected.Synopsis : null,
                    Categories = fill ? _selected.Categories : new List<string>(),
                    Rating = fill ? best?.Rating : null,
                    RatingsCount = fill ? best?.RatingsCount : null
                };
            }

            if (cover != null)
            {
                try
                {
                    await BookService.UpdateCoverAsync(Book.Id, cover);
                    Book.CoverCacheBuster = DateTime.UtcNow.Ticks;
                }
                catch (Exception ex)
                {
                    // Los metadatos ya se guardaron: se avisa y el diálogo sigue abierto para elegir otra portada
                    ToastService.ShowError($"{TranslationService["CoverSearch_ApplyError"]} {ex.Message}");
                    return;
                }
            }
            DialogService.Close(true);
        }
        finally
        {
            _isSaving = false;
        }
    }
}
