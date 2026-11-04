using FluentAssertions;
using QuimeraReader.Application.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class SearchQueryBuilderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("¿?!-:()\"")]
    public void Build_WithoutSearchableWords_ReturnsNull(string? input)
    {
        SearchQueryBuilder.Build(input).Should().BeNull();
    }

    [Fact]
    public void Build_EachWordBecomesAPrefixTerm_AndSpecialCharsAreRemoved()
    {
        // Comillas, dos puntos o paréntesis no deben romper la sintaxis de FTS5
        SearchQueryBuilder.Build("Harry \"Potter\": (Rowling)").Should().Be("\"harry\"* AND \"potter\"* AND \"rowling\"*");
    }

    [Fact]
    public void Build_KeepsAccentedLetters_TheTokenizerRemovesDiacritics()
    {
        SearchQueryBuilder.Build("Zafón").Should().Be("\"zafón\"*");
    }

    [Fact]
    public void Build_UppercaseInitials_AlsoMatchSeparatedInitials()
    {
        SearchQueryBuilder.Build("JK Rowling").Should().Be("(\"jk\"* OR (\"j\" AND \"k\")) AND \"rowling\"*");
    }
}
