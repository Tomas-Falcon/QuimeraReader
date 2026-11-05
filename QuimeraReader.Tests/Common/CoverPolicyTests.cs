using FluentAssertions;
using QuimeraReader.Application.Common;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class CoverPolicyTests
{
    // Cabecera PNG mínima: firma + IHDR con ancho y alto
    private static byte[] Png(int width, int height)
    {
        var d = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(d, 0);
        d[16] = (byte)(width >> 24); d[17] = (byte)(width >> 16); d[18] = (byte)(width >> 8); d[19] = (byte)width;
        d[20] = (byte)(height >> 24); d[21] = (byte)(height >> 16); d[22] = (byte)(height >> 8); d[23] = (byte)height;
        return d;
    }

    // JPEG mínimo: SOI, un APP0 que hay que saltar y un SOF0 con alto y ancho
    private static byte[] Jpeg(int width, int height) => new byte[]
    {
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    };

    [Fact]
    public void GetImageSize_ReadsPngAndJpegHeaders()
    {
        CoverPolicy.GetImageSize(Png(600, 900)).Should().Be((600, 900));
        CoverPolicy.GetImageSize(Jpeg(800, 1226)).Should().Be((800, 1226));
        CoverPolicy.GetImageSize(new byte[40]).Should().BeNull();
    }

    [Fact]
    public void Choose_KeepsTheEpubCover_OverASmallOnlineThumbnail()
    {
        var epub = Jpeg(600, 900);
        CoverPolicy.Choose(epub, Jpeg(128, 196)).Should().BeSameAs(epub);
    }

    [Fact]
    public void Choose_TakesTheOnlineCover_WhenClearlyLarger_OrWhenTheEpubHasNone()
    {
        var online = Jpeg(800, 1226);
        CoverPolicy.Choose(Jpeg(300, 450), online).Should().BeSameAs(online);
        CoverPolicy.Choose(null, online).Should().BeSameAs(online);
        // Parecida en tamaño: manda la del EPUB
        var epub = Png(600, 900);
        CoverPolicy.Choose(epub, Jpeg(640, 980)).Should().BeSameAs(epub);
    }

    [Fact]
    public void Choose_KeepsTheEpubCover_WhenItsSizeCannotBeRead()
    {
        var unknown = new byte[40];
        CoverPolicy.Choose(unknown, Jpeg(800, 1226)).Should().BeSameAs(unknown);
    }

    [Fact]
    public void ShouldReplace_OnlyForAMissingOrClearlySmallerCover()
    {
        CoverPolicy.ShouldReplace(null, Jpeg(128, 196)).Should().BeTrue();
        CoverPolicy.ShouldReplace(Jpeg(600, 900), Jpeg(128, 196)).Should().BeFalse();
        CoverPolicy.ShouldReplace(Jpeg(128, 196), Jpeg(800, 1226)).Should().BeTrue();
    }

    [Fact]
    public void GoogleCoverUrl_AsksForTheLargeImageWithoutThePageCurl()
    {
        CoverPolicy.GoogleCoverUrl("http://books.google.com/books/content?id=X&printsec=frontcover&img=1&zoom=1&edge=curl&source=gbs_api")
            .Should().Be("https://books.google.com/books/content?id=X&printsec=frontcover&img=1&zoom=1&source=gbs_api&fife=w800");
        CoverPolicy.GoogleCoverUrl(null).Should().BeNull();
    }

    [Theory]
    [InlineData("James S. A. Corey", new[] { "James S.A. Corey" }, true)]
    [InlineData("Martha Wells", new[] { "MARTHA WELLS" }, true)]
    [InlineData("María Elena Walsh", new[] { "Maria Elena Walsh" }, true)]   // sin tildes
    [InlineData("James S. A. Corey", new[] { "Florian Dalwigk" }, false)]    // "Drive" -> libro de hacking
    [InlineData("James S. A. Corey", new[] { "James Q. Glenn" }, false)]     // mismo nombre, otro apellido
    [InlineData("James S. A. Corey", new[] { "Corey, James S. A." }, true)]  // apellido primero
    [InlineData(null, new[] { "Quien sea" }, true)]                          // sin autor conocido no se puede descartar
    [InlineData("Dale Carnegie", new string[0], true)]                       // el resultado no trae autores
    public void AuthorsMatch_RejectsBooksByAnotherAuthor(string? known, string[] candidates, bool expected)
    {
        CoverPolicy.AuthorsMatch(known, candidates).Should().Be(expected);
    }
}
