using System.Collections.Generic;
using FluentAssertions;
using QuimeraReader.Domain.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class TextSimilarityUtilsTests
{
    [Theory]
    [InlineData("Harry Potter", "Harry Potter", 100.0)]
    [InlineData("El Aliento de los Dioses", "el aliento de los dioses", 100.0)]
    [InlineData("", "Harry Potter", 0.0)]
    [InlineData(null, "Harry Potter", 0.0)]
    [InlineData("Harry Potter", null, 0.0)]
    public void CalculateSimilarity_ExactOrEmptyCases_ShouldReturnExpectedScores(string? source, string? target, double expected)
    {
        // Act
        var score = TextSimilarityUtils.CalculateSimilarity(source?.ToLowerInvariant(), target?.ToLowerInvariant());

        // Assert
        score.Should().Be(expected);
    }

    [Fact]
    public void CalculateSimilarity_MinorTypo_ShouldYieldHighSimilarity()
    {
        // "Quimera Reader" vs "Quimera Reade"
        var score = TextSimilarityUtils.CalculateSimilarity("quimera reader", "quimera reade");
        score.Should().BeGreaterThan(90.0);
    }

    [Fact]
    public void CalculateSimilarity_CompletelyDifferentStrings_ShouldYieldLowSimilarity()
    {
        var score = TextSimilarityUtils.CalculateSimilarity("fundacion", "juego de tronos");
        score.Should().BeLessThan(30.0);
    }

    [Fact]
    public void GenerateTrigrams_WhenLessThanThreeWords_ShouldReturnEmptySet()
    {
        var trigrams = TextSimilarityUtils.GenerateTrigrams("Hola Mundo");
        trigrams.Should().BeEmpty();
    }

    [Fact]
    public void GenerateTrigrams_WithLongPhrase_ShouldExtractConsecutiveTriplets()
    {
        // Arrange
        var text = "El hombre de negro huía a través del desierto";

        // Act
        var trigrams = TextSimilarityUtils.GenerateTrigrams(text);

        // Assert
        trigrams.Should().NotBeEmpty();
        trigrams.Should().Contain("el hombre de");
        trigrams.Should().Contain("hombre de negro");
        trigrams.Should().Contain("negro huía a");
    }

    [Theory]
    [InlineData("Dune - Parte 1.mp3", "Dune", true)]
    [InlineData("Elantris_capitulo_01.mp3", "Elantris", true)]
    [InlineData("Brandon Sanderson - El Aliento de los Dioses - CD 1.mp3", "El Aliento de los Dioses", true)]
    [InlineData("track_01.mp3", "Track", false)]
    [InlineData("audio01.mp3", "Audio", false)]
    [InlineData("capitulo_primero.mp3", "El Capitulo", false)]
    [InlineData("Random Song.mp3", "Dune", false)]
    [InlineData("01 - Track 1.mp3", "Harry Potter", false)]
    public void TitleMatchesFileName_ShouldDistinguishLegitimateTitlesFromGenericNoise(string fileName, string bookTitle, bool expected)
    {
        var result = TextSimilarityUtils.TitleMatchesFileName(fileName, bookTitle);
        result.Should().Be(expected);
    }
}
