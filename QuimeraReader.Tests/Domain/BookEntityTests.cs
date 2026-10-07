using System;
using System.Collections.Generic;
using FluentAssertions;
using QuimeraReader.Domain.Entities;
using Xunit;

namespace QuimeraReader.Tests.Domain;

public class BookEntityTests
{
    [Fact]
    public void NewBook_ShouldInitializeCollectionsAsEmptyLists()
    {
        // Act
        var book = new Book();

        // Assert
        book.Authors.Should().NotBeNull().And.BeEmpty();
        book.Categories.Should().NotBeNull().And.BeEmpty();
        book.AudioTracks.Should().NotBeNull().And.BeEmpty();
        book.Annotations.Should().NotBeNull().And.BeEmpty();
        book.IsMetadataComplete.Should().BeFalse();
        book.IsAvailableOffline.Should().BeFalse();
        book.IsReadInHardcover.Should().BeFalse();
    }

    [Fact]
    public void Book_ShouldAssociateAuthorsAndCategoriesCorrectly()
    {
        // Arrange
        var book = new Book { Id = 10, Title = "El Nombre del Viento" };
        var author = new Author { Id = 1, Name = "Patrick Rothfuss" };
        var category = new Category { Id = 2, Name = "Fantasía Épica" };

        // Act
        book.Authors.Add(new BookAuthor { BookId = book.Id, AuthorId = author.Id, Author = author, Role = "Autor Principal" });
        book.Categories.Add(new BookCategory { BookId = book.Id, CategoryId = category.Id, Category = category });

        // Assert
        book.Authors.Should().HaveCount(1);
        book.Authors.Should().ContainSingle(ba => ba.Author!.Name == "Patrick Rothfuss" && ba.Role == "Autor Principal");
        book.Categories.Should().HaveCount(1);
        book.Categories.Should().ContainSingle(bc => bc.Category!.Name == "Fantasía Épica");
    }

    [Fact]
    public void Book_ShouldTrackMultipleAudioTracksInOrder()
    {
        // Arrange
        var book = new Book { Id = 1, Title = "Audiobook Test" };

        // Act
        book.AudioTracks.Add(new BookAudioTrack { TrackNumber = 1, FilePath = "/media/audio_ch1.mp3", DurationSeconds = 1200 });
        book.AudioTracks.Add(new BookAudioTrack { TrackNumber = 2, FilePath = "/media/audio_ch2.mp3", DurationSeconds = 1500 });

        // Assert
        book.AudioTracks.Should().HaveCount(2);
        book.AudioTracks.Should().BeInAscendingOrder(t => t.TrackNumber);
    }
}
