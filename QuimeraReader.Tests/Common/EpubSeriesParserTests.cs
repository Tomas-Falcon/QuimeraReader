using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using QuimeraReader.Application.Common;
using QuimeraReader.Infrastructure.Services;
using Xunit;

namespace QuimeraReader.Tests.Common;

public class EpubSeriesParserTests
{
    [Fact]
    public void Calibre_SeriesAndIndex()
    {
        var info = EpubSeriesParser.Parse(new[]
        {
            new EpubMeta("calibre:series", "Matabot"),
            new EpubMeta("calibre:series_index", "1.0")
        });

        info.Should().Be(new EpubSeriesInfo("Matabot", 1));
    }

    [Fact]
    public void Epub3_BelongsToCollection_PrefersSeriesType_AndReadsPosition()
    {
        var info = EpubSeriesParser.Parse(new[]
        {
            new EpubMeta(null, "Colección Bolsillo", Id: "c0", Property: "belongs-to-collection"),
            new EpubMeta(null, "set", Refines: "#c0", Property: "collection-type"),
            new EpubMeta(null, "Percy Jackson", Id: "c1", Property: "belongs-to-collection"),
            new EpubMeta(null, "series", Refines: "#c1", Property: "collection-type"),
            new EpubMeta(null, "2", Refines: "#c1", Property: "group-position")
        });

        info.Should().Be(new EpubSeriesInfo("Percy Jackson", 2));
    }

    [Theory]
    [InlineData("1,5", 1.5)]
    [InlineData("  3 ", 3.0)]
    [InlineData("0", null)]      // Calibre usa 0 cuando no hay número real
    [InlineData("abc", null)]
    public void Volume_IsParsedLeniently(string raw, double? expected)
    {
        EpubSeriesParser.Parse(new[] { new EpubMeta("calibre:series", "Saga"), new EpubMeta("calibre:series_index", raw) })!
            .Volume.Should().Be(expected);
    }

    [Fact]
    public void NoSeriesMetadata_ReturnsNull()
    {
        EpubSeriesParser.Parse(new[] { new EpubMeta("calibre:title_sort", "Algo") }).Should().BeNull();
        EpubSeriesParser.Parse(new[] { new EpubMeta("calibre:series", "   ") }).Should().BeNull();
    }

    [Fact]
    public void SeriesName_IsNormalized()
    {
        EpubSeriesParser.Parse(new[] { new EpubMeta("calibre:series", "  Las   crónicas de   Narnia ") })!
            .Name.Should().Be("Las crónicas de Narnia");
    }

    [Fact]
    public void OpfReader_ReadsSeriesFromARealEpubFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-{System.Guid.NewGuid():N}.epub");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(zip, "META-INF/container.xml",
                    "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
                Write(zip, "OEBPS/content.opf",
                    "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">" +
                    "<dc:title>Sistemas críticos</dc:title><meta name=\"calibre:series_index\" content=\"1\"/><meta name=\"calibre:series\" content=\"Matabot\"/></metadata></package>");
            }

            EpubOpfReader.ReadSeries(path).Should().Be(new EpubSeriesInfo("Matabot", 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
