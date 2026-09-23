using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Entities;
using Whisper.net;

namespace QuimeraReader.Infrastructure.Services;

public class AudioMatchingService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AudioMatchingService> _logger;

    public AudioMatchingService(AppDbContext dbContext, ILogger<AudioMatchingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int?> TryMatchAudioToBookAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        string fileName = Path.GetFileNameWithoutExtension(audioFilePath);
        var books = await _dbContext.Books.Include(b => b.Authors).ThenInclude(a => a.Author).ToListAsync(cancellationToken);
        
        var bestMatch = books
            .Select(b => new { Book = b, Score = CalculateSimilarity(fileName.ToLower(), b.Title.ToLower()) })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (bestMatch != null && bestMatch.Score > 85.0)
        {
            _logger.LogInformation("Audio emparejado por similitud de nombre ({Score}%): {File} -> {BookTitle}", 
                Math.Round(bestMatch.Score, 2), fileName, bestMatch.Book.Title);
            return bestMatch.Book.Id;
        }

        var explicitMatch = books.FirstOrDefault(b => fileName.ToLower().Contains(b.Title.ToLower()) && b.Title.Length > 5);
        if (explicitMatch != null)
        {
            _logger.LogInformation("Audio emparejado por contención de título: {File} -> {BookTitle}", fileName, explicitMatch.Title);
            return explicitMatch.Id;
        }

        _logger.LogInformation("Iniciando emparejamiento por contenido (Whisper Parcial) para {File}...", fileName);
        return await MatchByContentAsync(audioFilePath, books, cancellationToken);
    }

    private async Task<int?> MatchByContentAsync(string audioFilePath, System.Collections.Generic.List<Book> allBooks, CancellationToken cancellationToken)
    {
        var modelPathSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath", cancellationToken);
        string modelPath = modelPathSetting?.Value ?? "ggml-base.bin";

        if (!File.Exists(modelPath)) return null;

        string tempWavFile = Path.Combine(Path.GetTempPath(), $"_{Guid.NewGuid()}_partial.wav");

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{audioFilePath}\" -t 900 -ar 16000 -ac 1 -c:a pcm_s16le -y \"{tempWavFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return null;
            await process.WaitForExitAsync(cancellationToken);

            if (!File.Exists(tempWavFile)) return null;

            string extractedText = string.Empty;
            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder().WithLanguage("auto").Build();
            using var fileStream = File.OpenRead(tempWavFile);

            await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken))
            {
                extractedText += result.Text + " ";
                if (cancellationToken.IsCancellationRequested) break;
            }

            if (string.IsNullOrWhiteSpace(extractedText)) return null;
            string normalizedAudioText = extractedText.ToLowerInvariant();

            var contentMatch = allBooks
                .Select(b => new 
                { 
                    Book = b, 
                    Hits = (normalizedAudioText.Contains(b.Title.ToLower()) ? 10 : 0) + 
                           (b.Authors.Any(a => a.Author != null && normalizedAudioText.Contains(a.Author.Name.ToLower())) ? 5 : 0)
                })
                .Where(x => x.Hits >= 10)
                .OrderByDescending(x => x.Hits)
                .FirstOrDefault();

            if (contentMatch != null)
            {
                _logger.LogInformation("Audio emparejado por CONTENIDO: {File} -> {BookTitle}", Path.GetFileName(audioFilePath), contentMatch.Book.Title);
                return contentMatch.Book.Id;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en emparejamiento por contenido.");
            return null;
        }
        finally
        {
            if (File.Exists(tempWavFile)) try { File.Delete(tempWavFile); } catch { }
        }
    }

    private double CalculateSimilarity(string source, string target)
    {
        if (source == null || target == null || source.Length == 0 || target.Length == 0) return 0.0;
        if (source == target) return 100.0;
        int n = source.Length, m = target.Length;
        int[,] d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return (1.0 - ((double)d[n, m] / Math.Max(source.Length, target.Length))) * 100.0;
    }
}
