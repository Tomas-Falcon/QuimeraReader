using Microsoft.EntityFrameworkCore;
using QuimeraReader.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace QuimeraReader.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<Book> Books { get; }
    DbSet<Author> Authors { get; }
    DbSet<BookAuthor> BookAuthors { get; }
    DbSet<SyncMap> SyncMaps { get; }
    DbSet<BookAudioTrack> BookAudioTracks { get; }
    DbSet<UnmatchedAudioTrack> UnmatchedAudioTracks { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    DbSet<Universe> Universes { get; }
    DbSet<Series> Series { get; }
    DbSet<Category> Categories { get; }
    DbSet<BookCategory> BookCategories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
