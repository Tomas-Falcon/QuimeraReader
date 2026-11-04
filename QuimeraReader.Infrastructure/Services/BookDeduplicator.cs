using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Application.Common;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

/// <summary>
/// Une libros repetidos en uno solo sin perder nada de lo que hizo el usuario:
/// - mismo hash de archivo (el mismo EPUB importado dos veces);
/// - libros sin archivo que tienen una copia con archivo con el mismo título y primer autor.
/// El que se queda recibe estado, progreso, sin conexión, notas, audio, saga y universo de los que se borran.
/// Los archivos en disco no se tocan.
/// </summary>
public static class BookDeduplicator
{
    public class Report
    {
        public bool Applied { get; set; }
        public bool DeleteOrphans { get; set; }
        public int TotalBooks { get; set; }
        public int BooksWithHash { get; set; }
        public int HashGroups { get; set; }
        public int HashDuplicates { get; set; }
        public int MissingMergedIntoCopy { get; set; }
        /// <summary>Sin archivo, sin copia y sin nada del usuario: se borran solo con deleteOrphans.</summary>
        public int MissingOrphans { get; set; }
        public int OrphansDeleted { get; set; }
        /// <summary>Sin archivo ni copia pero con progreso, notas, audio o sin conexión: se conservan.</summary>
        public List<string> MissingKeptWithUserData { get; set; } = new();
        public int TransferredUserData { get; set; }
        public int Deleted { get; set; }
    }

    private class Row
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string EpubFilePath { get; set; } = "";
        public string? ContentHash { get; set; }
        public string? ReadingStatus { get; set; }
        public DateTime? LastReadAt { get; set; }
        public double? PercentageCompleted { get; set; }
        public double? CurrentAudioPosition { get; set; }
        public bool IsAvailableOffline { get; set; }
        public string Author { get; set; } = "";
        public bool Present { get; set; }
        public bool HasNotes { get; set; }
        public bool HasAudio { get; set; }

        public bool HasUserData => IsAvailableOffline || HasNotes || HasAudio || LastReadAt != null
                                   || (PercentageCompleted ?? 0) > 0 || (CurrentAudioPosition ?? 0) > 0
                                   || (!string.IsNullOrEmpty(ReadingStatus) && ReadingStatus != "Unread");

        public int Score => (Present ? 1000 : 0) + (HasNotes ? 100 : 0) + (HasAudio ? 50 : 0) + (HasUserData ? 10 : 0);
    }

    public static async Task<Report> RunAsync(AppDbContext db, string? libraryRoot, bool apply, bool deleteOrphans, CancellationToken cancellationToken = default)
    {
        var report = new Report { Applied = apply, DeleteOrphans = deleteOrphans };

        var rows = await db.Books.AsNoTracking()
            .Select(b => new Row
            {
                Id = b.Id, Title = b.Title, EpubFilePath = b.EpubFilePath, ContentHash = b.ContentHash,
                ReadingStatus = b.ReadingStatus, LastReadAt = b.LastReadAt, PercentageCompleted = b.PercentageCompleted,
                CurrentAudioPosition = b.CurrentAudioPosition, IsAvailableOffline = b.IsAvailableOffline
            })
            .ToListAsync(cancellationToken);
        report.TotalBooks = rows.Count;

        var authors = (await db.BookAuthors.AsNoTracking().Select(ba => new { ba.BookId, ba.Author.Name }).ToListAsync(cancellationToken))
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Min(x => x.Name) ?? "");
        var withNotes = (await db.BookAnnotations.AsNoTracking().Select(a => a.BookId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var withAudio = (await db.BookAudioTracks.AsNoTracking().Select(t => t.BookId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row.Author = authors.GetValueOrDefault(row.Id) ?? "";
            row.HasNotes = withNotes.Contains(row.Id);
            row.HasAudio = withAudio.Contains(row.Id);
            row.Present = LibraryPathResolver.Resolve(row.EpubFilePath, libraryRoot) != null;
        }
        report.BooksWithHash = rows.Count(r => r.ContentHash != null);

        // keeper -> libros que se funden en él
        var merges = new Dictionary<int, List<int>>();
        var removed = new HashSet<int>();

        foreach (var group in rows.Where(r => r.ContentHash != null).GroupBy(r => r.ContentHash!).Where(g => g.Count() > 1))
        {
            report.HashGroups++;
            var ordered = group.OrderByDescending(r => r.Score).ThenBy(r => r.Id).ToList();
            foreach (var dup in ordered.Skip(1))
            {
                AddMerge(merges, ordered[0].Id, dup.Id);
                removed.Add(dup.Id);
                report.HashDuplicates++;
            }
        }

        static string Key(Row r) => TextNormalizer.Fold(r.Title) + "|" + TextNormalizer.Fold(r.Author);
        var presentByKey = rows.Where(r => r.Present && !removed.Contains(r.Id))
            .GroupBy(Key).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Score).ThenBy(r => r.Id).First());

        var orphans = new List<int>();
        foreach (var row in rows.Where(r => !r.Present && !removed.Contains(r.Id)))
        {
            if (presentByKey.TryGetValue(Key(row), out var keeper))
            {
                AddMerge(merges, keeper.Id, row.Id);
                removed.Add(row.Id);
                report.MissingMergedIntoCopy++;
            }
            else if (row.HasUserData)
            {
                report.MissingKeptWithUserData.Add($"{row.Id} | {row.Title} | {row.Author}");
            }
            else
            {
                orphans.Add(row.Id);
                report.MissingOrphans++;
            }
        }

        var rowsById = rows.ToDictionary(r => r.Id);
        report.TransferredUserData = merges.Values.SelectMany(v => v).Count(id => rowsById[id].HasUserData);

        if (!apply) return report;

        foreach (var chunk in merges.Chunk(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (keeperId, dupIds) in chunk)
            {
                await MergeAsync(db, keeperId, dupIds, cancellationToken);
                report.Deleted += dupIds.Count;
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        if (deleteOrphans)
        {
            foreach (var chunk in orphans.Chunk(500))
            {
                var books = await db.Books.Where(b => chunk.Contains(b.Id)).ToListAsync(cancellationToken);
                db.Books.RemoveRange(books);
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                report.OrphansDeleted += books.Count;
            }
        }

        return report;
    }

    private static void AddMerge(Dictionary<int, List<int>> merges, int keeperId, int dupId)
    {
        if (!merges.TryGetValue(keeperId, out var list)) merges[keeperId] = list = new List<int>();
        list.Add(dupId);
    }

    /// <summary>Pasa al libro que se queda todo lo del usuario que tengan los duplicados y los borra.</summary>
    public static async Task MergeAsync(AppDbContext db, int keeperId, IReadOnlyCollection<int> dupIds, CancellationToken cancellationToken = default)
    {
        var keeper = await db.Books.Include(b => b.AudioTracks).Include(b => b.SyncMap).FirstAsync(b => b.Id == keeperId, cancellationToken);
        var dups = await db.Books.Include(b => b.AudioTracks).Include(b => b.Annotations).Include(b => b.SyncMap)
            .Where(b => dupIds.Contains(b.Id)).OrderByDescending(b => b.LastReadAt).ToListAsync(cancellationToken);

        foreach (var dup in dups)
        {
            // El progreso más reciente gana (misma regla que la sincronización)
            if (dup.LastReadAt != null && (keeper.LastReadAt == null || dup.LastReadAt > keeper.LastReadAt))
            {
                keeper.LastReadAt = dup.LastReadAt;
                keeper.CurrentEpubCfi = dup.CurrentEpubCfi ?? keeper.CurrentEpubCfi;
                keeper.PercentageCompleted = dup.PercentageCompleted ?? keeper.PercentageCompleted;
                keeper.CurrentAudioPosition = dup.CurrentAudioPosition ?? keeper.CurrentAudioPosition;
                keeper.CurrentAudioTrackNumber = dup.CurrentAudioTrackNumber ?? keeper.CurrentAudioTrackNumber;
            }
            if ((string.IsNullOrEmpty(keeper.ReadingStatus) || keeper.ReadingStatus == "Unread") && !string.IsNullOrEmpty(dup.ReadingStatus))
                keeper.ReadingStatus = dup.ReadingStatus;
            keeper.IsAvailableOffline |= dup.IsAvailableOffline;
            keeper.IsReadInHardcover |= dup.IsReadInHardcover;
            keeper.SeriesId ??= dup.SeriesId;
            keeper.SeriesVolume ??= dup.SeriesVolume;
            keeper.UniverseId ??= dup.UniverseId;
            keeper.ContentHash ??= dup.ContentHash;
            if (string.IsNullOrEmpty(keeper.CoverImagePath)) keeper.CoverImagePath = dup.CoverImagePath;

            foreach (var note in dup.Annotations.ToList()) note.BookId = keeper.Id;

            if (dup.AudioTracks.Count > 0)
            {
                int next = keeper.AudioTracks.Count == 0 ? 1 : keeper.AudioTracks.Max(t => t.TrackNumber) + 1;
                foreach (var track in dup.AudioTracks.OrderBy(t => t.TrackNumber).ToList())
                {
                    if (keeper.AudioTracks.Any(t => t.FilePath == track.FilePath)) continue;
                    track.BookId = keeper.Id;
                    track.TrackNumber = next++;
                }
            }

            if (keeper.SyncMap == null && dup.SyncMap != null) dup.SyncMap.BookId = keeper.Id;

            await db.ScannedSourceFiles.Where(f => f.BookId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.BookId, keeper.Id), cancellationToken);
        }

        // Las notas y pistas ya movidas no deben borrarse en cascada con el duplicado
        await db.SaveChangesAsync(cancellationToken);
        db.Books.RemoveRange(dups);
    }
}
