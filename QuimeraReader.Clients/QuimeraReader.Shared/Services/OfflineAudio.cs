using QuimeraReader.Shared.Models;

namespace QuimeraReader.Shared.Services;

/// <summary>
/// Convenciones de la copia offline del audio. La primera pista sigue guardándose como "audio" (Book.LocalAudioPath,
/// descargada de api/media/books/{id}/audio); las demás se descargan una a una y se guardan como "audio_t{n}".
/// </summary>
public static class OfflineAudio
{
    public const string FirstTrackFileType = "audio";
    private const string TrackFileTypePrefix = "audio_t";

    public static int FirstTrackNumber(Book book) =>
        book.AudioTracks.Count > 0 ? book.AudioTracks.Min(t => t.TrackNumber) : 1;

    /// <summary>Pistas que se descargan aparte de la primera</summary>
    public static IEnumerable<AudioTrackDto> ExtraTracks(Book book)
    {
        var first = FirstTrackNumber(book);
        return book.AudioTracks.Where(t => t.TrackNumber != first).OrderBy(t => t.TrackNumber);
    }

    /// <summary>Clave de la pista en el repositorio local (ILocalBookRepository.GetBookFileUrlAsync)</summary>
    public static string FileType(Book book, int trackNumber) =>
        trackNumber == FirstTrackNumber(book) ? FirstTrackFileType : TrackFileTypePrefix + trackNumber;

    public static int? TrackNumberFromFileType(string fileType) =>
        fileType.StartsWith(TrackFileTypePrefix, StringComparison.OrdinalIgnoreCase)
        && int.TryParse(fileType.AsSpan(TrackFileTypePrefix.Length), out var n) ? n : null;

    public static string TrackServerPath(int bookId, int trackNumber) => $"api/media/books/{bookId}/tracks/{trackNumber}";

    public static string LocalFileName(int bookId, AudioTrackDto track)
    {
        var ext = Path.GetExtension(track.FileName ?? "").ToLowerInvariant();
        if (ext.Length is < 2 or > 5) ext = ".mp3";
        return $"audio_{bookId}_t{track.TrackNumber}{ext}";
    }
}
