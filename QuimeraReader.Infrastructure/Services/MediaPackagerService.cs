using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

public class MediaPackagerService
{
        public async Task<bool> NormalizeAudioAsync(string sourcePath, string destPath)
    {
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                // Convert to mp3 with reasonable quality for voice (approx 64-96 kbps VBR)
                Arguments = $"-i \"{sourcePath}\" -c:a libmp3lame -q:a 5 -map 0:a:0 -y \"{destPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0 && File.Exists(destPath);
            }
        }
        catch { }
        return false;
    }

    public async Task<Stream> CreateAudiobookPackageAsync(Book book)
    {
        var audioTrack = book.AudioTracks.OrderBy(t => t.TrackNumber).FirstOrDefault();
        var audioFilePath = audioTrack?.FilePath;

        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("El archivo de audio no existe.");
        }

        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // 1. Agregar el archivo de audio
            var audioEntryName = Path.GetFileName(audioFilePath);
            var audioEntry = archive.CreateEntry(audioEntryName, CompressionLevel.NoCompression);
            using (var entryStream = audioEntry.Open())
            using (var fileStream = File.OpenRead(audioFilePath))
            {
                await fileStream.CopyToAsync(entryStream);
            }

            // 2. Generar el manifest.json (Estándar Readium Webpub para Audiobooks)
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var entryStream = manifestEntry.Open())
            {
                var manifest = new
                {
                    @context = "https://readium.org/webpub-manifest/context.jsonld",
                    metadata = new
                    {
                        @type = "http://schema.org/Audiobook",
                        title = book.Title
                    },
                    readingOrder = new[]
                    {
                        new { href = audioEntryName, type = GetMimeType(audioFilePath) }
                    }
                };

                await JsonSerializer.SerializeAsync(entryStream, manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    private string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4b" or ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };
    }
}
