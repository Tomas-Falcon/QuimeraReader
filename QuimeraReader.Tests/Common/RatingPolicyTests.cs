using FluentAssertions;
using QuimeraReader.Application.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class RatingPolicyTests
{
    [Fact]
    public void Best_TakesTheRatingWithMostVotes_IgnoringUnreliableOnes()
    {
        var best = RatingPolicy.Best(new[]
        {
            new RatingPolicy.Rating(5.0, 1, "GoogleBooks"),     // un solo voto: no cuenta
            new RatingPolicy.Rating(4.0, 3, "GoogleBooks"),
            new RatingPolicy.Rating(4.31, 3187, "Hardcover"),
            new RatingPolicy.Rating(4.2, 900, "OpenLibrary")
        });

        best.Should().Be(new RatingPolicy.Rating(4.31, 3187, "Hardcover"));
    }

    [Fact]
    public void Best_IsNull_WhenNoRatingHasEnoughVotes()
    {
        RatingPolicy.Best(new[] { new RatingPolicy.Rating(5.0, 1), new RatingPolicy.Rating(4.5, 4) }).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, 4.3, 3187, true)]   // sin puntuación
    [InlineData(5.0, null, 4.3, 3187, true)]    // la vieja sin votos guardados (el 5,0 de un voto)
    [InlineData(4.0, 400, 4.3, 3187, true)]     // más votos
    [InlineData(4.3, 3187, 4.0, 400, false)]    // menos votos
    [InlineData(null, null, 5.0, 1, false)]     // poco fiable: ni siquiera para rellenar
    public void ShouldReplace_OnlyWithAReliableRatingWithMoreVotes(double? current, int? currentVotes, double value, int votes, bool expected)
    {
        RatingPolicy.ShouldReplace(current, currentVotes, new RatingPolicy.Rating(value, votes)).Should().Be(expected);
    }
}
