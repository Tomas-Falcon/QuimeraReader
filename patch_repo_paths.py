import sys

def modify_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_shared = '''    private Book MapToShared(QuimeraReader.Domain.Entities.Book domainBook)
    {
        if (domainBook == null) return null;
        return new Book
        {
            Id = domainBook.Id,
            Title = domainBook.Title,
            CoverImagePath = domainBook.CoverImagePath,
            EpubFilePath = domainBook.EpubFilePath,
            IsAvailableOffline = domainBook.IsAvailableOffline,
            CurrentEpubCfi = domainBook.CurrentEpubCfi,
            PercentageCompleted = domainBook.PercentageCompleted,
            Authors = domainBook.Authors?.Select(a => a.Author?.Name ?? "").ToList() ?? new List<string>(),
            Categories = domainBook.Categories?.Select(c => c.Category?.Name ?? "").ToList() ?? new List<string>()
        };
    }'''

    new_shared = '''    private Book MapToShared(QuimeraReader.Domain.Entities.Book domainBook)
    {
        if (domainBook == null) return null!;
        return new Book
        {
            Id = domainBook.Id,
            Title = domainBook.Title,
            LocalCoverPath = domainBook.CoverImagePath,
            LocalEpubPath = domainBook.EpubFilePath,
            IsAvailableOffline = domainBook.IsAvailableOffline,
            CurrentEpubCfi = domainBook.CurrentEpubCfi,
            PercentageCompleted = domainBook.PercentageCompleted,
            Authors = domainBook.Authors?.Select(a => a.Author?.Name ?? "").ToList() ?? new List<string>(),
            Categories = domainBook.Categories?.Select(c => c.Category?.Name ?? "").ToList() ?? new List<string>()
        };
    }'''

    content = content.replace(old_shared, new_shared)

    old_dom = '''    private QuimeraReader.Domain.Entities.Book MapToDomain(Book sharedBook)
    {
        return new QuimeraReader.Domain.Entities.Book
        {
            Id = sharedBook.Id,
            Title = sharedBook.Title,
            CoverImagePath = sharedBook.CoverImagePath,
            EpubFilePath = sharedBook.EpubFilePath,
            IsAvailableOffline = sharedBook.IsAvailableOffline,
            CurrentEpubCfi = sharedBook.CurrentEpubCfi,
            PercentageCompleted = sharedBook.PercentageCompleted
        };
    }'''

    new_dom = '''    private QuimeraReader.Domain.Entities.Book MapToDomain(Book sharedBook)
    {
        return new QuimeraReader.Domain.Entities.Book
        {
            Id = sharedBook.Id,
            Title = sharedBook.Title,
            CoverImagePath = sharedBook.LocalCoverPath,
            EpubFilePath = sharedBook.LocalEpubPath,
            IsAvailableOffline = sharedBook.IsAvailableOffline,
            CurrentEpubCfi = sharedBook.CurrentEpubCfi ?? "",
            PercentageCompleted = sharedBook.PercentageCompleted
        };
    }'''

    content = content.replace(old_dom, new_dom)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

modify_file('QuimeraReader.Clients/QuimeraReader.Mobile/Data/LocalBookRepository.cs')