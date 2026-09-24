import sys
import re

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Add dependencies
    content = content.replace('using Microsoft.Extensions.Logging;', 'using Microsoft.Extensions.Logging;\nusing QuimeraReader.Shared.Interfaces;\nusing Microsoft.Extensions.DependencyInjection;')
    
    # Update constructor
    old_ctor = '''    private readonly HttpClient _httpClient;
    private readonly ILogger<BookService> _logger;

    public BookService(HttpClient httpClient, ILogger<BookService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }'''

    new_ctor = '''    private readonly HttpClient _httpClient;
    private readonly ILogger<BookService> _logger;
    private readonly INetworkStateService _networkState;
    private readonly IServiceProvider _serviceProvider;

    public BookService(HttpClient httpClient, ILogger<BookService> logger, INetworkStateService networkState, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _networkState = networkState;
        _serviceProvider = serviceProvider;
    }

    private ILocalBookRepository? GetLocalRepo() => _serviceProvider.GetService<ILocalBookRepository>();
'''
    content = content.replace(old_ctor, new_ctor)

    # Intercept GetBooksAsync
    old_getbooks = '''    public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null)
    {
        try
        {
            var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };'''

    new_getbooks = '''    public async Task<PaginatedResult<Book>> GetBooksAsync(int page = 1, int pageSize = 50, string? search = null, int[]? categoryIds = null, string? readingStatus = null, int? skip = null, int? take = null)
    {
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null)
                {
                    var localBooks = await localRepo.GetOfflineBooksAsync();
                    // Basic filtering for offline mode
                    if (!string.IsNullOrEmpty(search))
                        localBooks = localBooks.Where(b => b.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    
                    int s = skip ?? ((page - 1) * pageSize);
                    int t = take ?? pageSize;
                    var paged = localBooks.Skip(s).Take(t).ToList();
                    return new PaginatedResult<Book> { Items = paged, TotalCount = localBooks.Count };
                }
            }

            var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };'''
    content = content.replace(old_getbooks, new_getbooks)

    # Intercept GetBookByIdAsync
    old_getbook = '''    public async Task<Book?> GetBookByIdAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Book>($"api/Books/{id}");'''

    new_getbook = '''    public async Task<Book?> GetBookByIdAsync(int id)
    {
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null) return await localRepo.GetBookByIdAsync(id);
            }
            return await _httpClient.GetFromJsonAsync<Book>($"api/Books/{id}");'''
    content = content.replace(old_getbook, new_getbook)

    # Intercept UpdatePositionAsync
    old_updatepos = '''    public async Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null, int? audioTrackNumber = null)
    {
        try
        {
            var request = new UpdatePositionRequest'''
    new_updatepos = '''    public async Task UpdatePositionAsync(int bookId, string? epubCfi = null, double? audioPosition = null, double? percentage = null, int? audioTrackNumber = null)
    {
        try
        {
            if (_networkState.IsOffline)
            {
                var localRepo = GetLocalRepo();
                if (localRepo != null) 
                {
                    await localRepo.UpdateProgressAsync(bookId, epubCfi ?? "", audioPosition, percentage);
                    return;
                }
            }
            var request = new UpdatePositionRequest'''
    content = content.replace(old_updatepos, new_updatepos)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Shared/Services/BookService.cs')