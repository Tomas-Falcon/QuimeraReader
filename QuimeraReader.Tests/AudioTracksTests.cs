using Xunit;
using QuimeraReader.Domain.Entities;
using System.Linq;
using System.Collections.Generic;

namespace QuimeraReader.Tests;

public class AudioTracksTests
{
    [Fact]
    public void Book_Should_SupportMultipleAudioTracks()
    {
        // Arrange
        var book = new Book
        {
            Id = 1,
            Title = "Test Book",
            AudioTracks = new List<BookAudioTrack>()
        };

        // Act
        book.AudioTracks.Add(new BookAudioTrack { TrackNumber = 1, FilePath = "track1.mp3" });
        book.AudioTracks.Add(new BookAudioTrack { TrackNumber = 2, FilePath = "track2.mp3" });

        // Assert
        Assert.Equal(2, book.AudioTracks.Count);
        Assert.Equal("track1.mp3", book.AudioTracks.First(t => t.TrackNumber == 1).FilePath);
    }
}
