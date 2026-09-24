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
    Task EnsureCreatedAsync();
}
