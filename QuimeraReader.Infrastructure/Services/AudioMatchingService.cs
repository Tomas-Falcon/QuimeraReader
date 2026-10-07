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

public class AudioMatchingService : QuimeraReader.Application.Interfaces.IAudioMatchingService
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
        string rawFileName = Path.GetFileNameWithoutExtension(audioFilePath).ToLowerInvariant();
        string fileName = rawFileName.Replace("_", " ").Replace("-", " ");
        
        var allBooks = await _dbContext.Books.Select(b => new { b.Id, b.Title }).ToListAsync(cancellationToken);
        
        int? preCandidateId = null;

        var explicitMatch = allBooks.FirstOrDefault(b => b.Title.Length > 5 && fileName.Contains(b.Title.ToLower()));
        if (explicitMatch != null)
        {
            _logger.LogInformation("Candidato inicial por título: {File} -> {BookTitle}. Validando por contenido...", fileName, explicitMatch.Title);
            preCandidateId = explicitMatch.Id;
        }
        else
        {
            var bestMatch = allBooks
                .Select(b => new { Book = b, Score = CalculateSimilarity(fileName, b.Title.ToLower()) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (bestMatch != null && bestMatch.Score > 85.0)
            {
                _logger.LogInformation("Candidato inicial por similitud ({Score}%): {File} -> {BookTitle}. Validando por contenido...", Math.Round(bestMatch.Score, 2), fileName, bestMatch.Book.Title);
                preCandidateId = bestMatch.Book.Id;
            }
        }

        _logger.LogInformation("Iniciando extracción Whisper para validación de contenido de {File}...", fileName);
        return await MatchByContentAsync(audioFilePath, preCandidateId, cancellationToken);
    }

    private async Task<int?> MatchByContentAsync(string audioFilePath, int? preCandidateId, CancellationToken cancellationToken)
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

            // 1. Streaming Candidate Selection (Phase 1)
            string rawFileName = Path.GetFileNameWithoutExtension(audioFilePath).ToLowerInvariant();
            string spacedFileName = rawFileName.Replace("_", " ").Replace("-", " ");
            string cleanFileName = new string(spacedFileName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
            var fileWords = cleanFileName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Where(w => w.Length > 2)
                                       .ToList();

            var candidateScores = new Dictionary<int, double>();
            if (preCandidateId.HasValue) candidateScores[preCandidateId.Value] = 1000.0;

            var booksStream = _dbContext.Books.AsNoTracking().Select(b => new { b.Id, b.Title, b.EpubFilePath }).AsAsyncEnumerable();

            await foreach (var b in booksStream.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrEmpty(b.EpubFilePath)) continue;
                
                // Título hablado: solo suma puntos al candidato (NO asigna). Un título de una
                // palabra común (ej. "Sangre") aparece en casi cualquier transcripción.
                double spokenTitleBonus = 0;
                if (b.Title.Length > 4)
                {
                    var titleWordCount = b.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (normalizedAudioText.Contains(b.Title.ToLowerInvariant()))
                        spokenTitleBonus = titleWordCount >= 2 ? 100 : 10;
                }

                // Calculate Fuzzy Score
                string cleanTitle = new string(b.Title.ToLowerInvariant().Replace("_", " ").Replace("-", " ").Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
                double score = 0;
                foreach (var w in fileWords)
                {
                    if (cleanTitle.Contains(w)) score += 50;
                }
                score += CalculateSimilarity(cleanFileName, cleanTitle) + spokenTitleBonus;
                
                if (score > 30) // Arbitrary minimum to avoid sorting 140k
                {
                    candidateScores[b.Id] = candidateScores.TryGetValue(b.Id, out var existing) ? Math.Max(existing, score) : score;
                }
            }

            var topCandidateIds = candidateScores.OrderByDescending(kvp => kvp.Value).Take(20).Select(kvp => kvp.Key).ToList();

            // If empty, fallback to recent
            if (!topCandidateIds.Any())
            {
                var fallbackIds = await _dbContext.Books
                    .Where(b => !b.AudioTracks.Any() && !string.IsNullOrEmpty(b.EpubFilePath))
                    .OrderByDescending(b => b.Id)
                    .Take(20)
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                topCandidateIds.AddRange(fallbackIds);
            }

            // 2. Trigram Intersection (Phase 2)
            var whisperTrigrams = GenerateTrigrams(normalizedAudioText);
            
            var candidateBooks = await _dbContext.Books
                .Where(b => topCandidateIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Title, b.EpubFilePath })
                .ToListAsync(cancellationToken);

            int bestMatchId = 0;
            int maxTrigrams = 0;

            foreach (var candidate in candidateBooks)
            {
                if (string.IsNullOrEmpty(candidate.EpubFilePath) || !File.Exists(candidate.EpubFilePath)) continue;

                try
                {
                    var book = VersOne.Epub.EpubReader.ReadBook(candidate.EpubFilePath);
                    var sb = new System.Text.StringBuilder();
                    foreach (var textContentFile in book.ReadingOrder.Take(4))
                    {
                        sb.AppendLine(textContentFile.Content);
                    }
                    string epubRawText = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "<.*?>", string.Empty).ToLowerInvariant();
                    
                    var epubTrigrams = GenerateTrigrams(epubRawText);
                    
                    int matchCount = whisperTrigrams.Intersect(epubTrigrams).Count();
                    
                    if (matchCount > maxTrigrams)
                    {
                        maxTrigrams = matchCount;
                        bestMatchId = candidate.Id;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error leyendo EPUB para emparejamiento por Trigramas: {Path}", candidate.EpubFilePath);
                }
            }

            if (maxTrigrams >= 10 && bestMatchId > 0)
            {
                var matchedTitle = candidateBooks.First(b => b.Id == bestMatchId).Title;
                _logger.LogInformation("Audio emparejado por TRIGRAMAS ({Hits} secuencias idénticas): {File} -> {BookTitle}", maxTrigrams, Path.GetFileName(audioFilePath), matchedTitle);
                return bestMatchId;
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

    private HashSet<string> GenerateTrigrams(string text)
        => QuimeraReader.Domain.Common.TextSimilarityUtils.GenerateTrigrams(text);

    private double CalculateSimilarity(string source, string target)
        => QuimeraReader.Domain.Common.TextSimilarityUtils.CalculateSimilarity(source, target);
}


