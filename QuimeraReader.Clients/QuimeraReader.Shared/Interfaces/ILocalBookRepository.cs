using System.Collections.Generic;
using System.Threading.Tasks;
using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Interfaces;

public interface ILocalBookRepository
{
    Task<List<Book>> GetOfflineBooksAsync();
    Task<Book?> GetBookByIdAsync(int id);
    Task SaveBookAsync(Book book);
    Task SaveBooksAsync(IEnumerable<Book> books);
    Task DeleteBookAsync(int id);
    Task UpdateProgressAsync(int id, string cfi, double? audioPosition, double? percentage);
    Task QueueAnnotationAsync(int bookId, string cfiRange, string selectedText, string colorHex, string note);
    Task<List<AnnotationQueueItem>> GetQueuedAnnotationsAsync();
    Task RemoveQueuedAnnotationAsync(string id);
    Task EnsureCreatedAsync();
    Task<List<AnnotationDto>> GetAnnotationsAsync(int bookId);
    Task SyncAnnotationsAsync(int bookId, List<AnnotationDto> annotations);
}
