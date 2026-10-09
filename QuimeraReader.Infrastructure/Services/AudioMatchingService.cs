using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Domain.Common;
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
        string rawFileName = Path.GetFileNameWithoutExtension(audioFilePath);
        string normalizedFileName = TextSimilarityUtils.NormalizeComparableText(rawFileName);

        if (string.IsNullOrWhiteSpace(normalizedFileName)) return null;

        var allBooks = await _dbContext.Books
            .Where(b => !string.IsNullOrEmpty(b.EpubFilePath))
            .Select(b => new { b.Id, b.Title, b.EpubFilePath })
            .ToListAsync(cancellationToken);

        int? preCandidateId = null;

        // 1. Coincidencia exacta o contenida completa en el nombre del archivo
        var explicitMatch = allBooks.FirstOrDefault(b => TextSimilarityUtils.TitleMatchesFileName(rawFileName, b.Title));
        if (explicitMatch != null)
        {
            _logger.LogInformation("Candidato inicial por título en nombre de archivo: {File} -> {BookTitle}. Validando por contenido...", rawFileName, explicitMatch.Title);
            preCandidateId = explicitMatch.Id;
        }
        else
        {
            // Solo considerar si el parecido con el título completo es muy alto (>= 88%)
            var bestFuzzyMatch = allBooks
                .Select(b => new { Book = b, Score = CalculateSimilarity(normalizedFileName, TextSimilarityUtils.NormalizeComparableText(b.Title)) })
                .Where(x => x.Score >= 88.0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (bestFuzzyMatch != null)
            {
                _logger.LogInformation("Candidato inicial por similitud ({Score}%): {File} -> {BookTitle}. Validando por contenido...", Math.Round(bestFuzzyMatch.Score, 2), rawFileName, bestFuzzyMatch.Book.Title);
                preCandidateId = bestFuzzyMatch.Book.Id;
            }
        }

        _logger.LogInformation("Iniciando extracción Whisper para validación de contenido de {File}...", rawFileName);
        return await MatchByContentAsync(audioFilePath, preCandidateId, cancellationToken);
    }

    private async Task<int?> MatchByContentAsync(string audioFilePath, int? preCandidateId, CancellationToken cancellationToken)
    {
        var modelPathSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath", cancellationToken);
        string modelPath = modelPathSetting?.Value ?? "ggml-base.bin";

        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("Modelo Whisper no encontrado en {Path}. No se puede validar el audio por contenido.", modelPath);
            return null;
        }

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

            // 1. Filtrado de candidatos potenciales
            string rawFileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var fileWords = TextSimilarityUtils.NormalizeComparableText(rawFileName)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();

            var candidateScores = new Dictionary<int, double>();
            if (preCandidateId.HasValue) candidateScores[preCandidateId.Value] = 100.0;

            var booksStream = _dbContext.Books.AsNoTracking()
                .Where(b => !string.IsNullOrEmpty(b.EpubFilePath))
                .Select(b => new { b.Id, b.Title, b.EpubFilePath })
                .AsAsyncEnumerable();

            await foreach (var b in booksStream.WithCancellation(cancellationToken))
            {
                double spokenTitleBonus = 0;
                var normTitle = TextSimilarityUtils.NormalizeComparableText(b.Title);
                if (normTitle.Length > 4)
                {
                    var titleWords = normTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // Solo si el título tiene al menos 2 palabras y se menciona completo en el audio
                    if (titleWords.Length >= 2 && normalizedAudioText.Contains(normTitle))
                    {
                        spokenTitleBonus = 80;
                    }
                }

                double score = spokenTitleBonus;
                foreach (var w in fileWords)
                {
                    if (normTitle.Contains(w)) score += 20;
                }

                if (score >= 40)
                {
                    candidateScores[b.Id] = candidateScores.TryGetValue(b.Id, out var existing) ? Math.Max(existing, score) : score;
                }
            }

            // Si ningún libro alcanza puntuación preliminar mínima, NO forzar búsqueda ciega
            if (!candidateScores.Any())
            {
                _logger.LogInformation("No se hallaron candidatos con similitud de título o mención en audio para {File}.", Path.GetFileName(audioFilePath));
                return null;
            }

            var topCandidateIds = candidateScores.OrderByDescending(kvp => kvp.Value).Take(10).Select(kvp => kvp.Key).ToList();

            // 2. Intersección de trigramas (Validación estricta de contenido)
            var whisperTrigrams = GenerateTrigrams(normalizedAudioText);
            if (whisperTrigrams.Count < 5)
            {
                _logger.LogWarning("El audio transcrito generó muy pocos trigramas ({Count}). No hay suficiente texto para validar.", whisperTrigrams.Count);
                return null;
            }

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
                    foreach (var textContentFile in book.ReadingOrder.Take(10))
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

            double coverage = whisperTrigrams.Any() ? (double)maxTrigrams / whisperTrigrams.Count : 0;
            
            // Requiere un mínimo absoluto de trigramas (>= 15) y cobertura >= 10%
            if (maxTrigrams >= 15 && coverage >= 0.10 && bestMatchId > 0)
            {
                var matchedTitle = candidateBooks.First(b => b.Id == bestMatchId).Title;
                _logger.LogInformation("Audio emparejado por TRIGRAMAS ({Hits} secuencias idénticas, {Coverage:P2} cobertura): {File} -> {BookTitle}", maxTrigrams, coverage, Path.GetFileName(audioFilePath), matchedTitle);
                return bestMatchId;
            }

            _logger.LogInformation("Audio descartado para asociación automática ({File}): máxima coincidencia de trigramas fue {Hits} ({Coverage:P2} cobertura), insuficiente para asociar con seguridad.", Path.GetFileName(audioFilePath), maxTrigrams, coverage);
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
        => TextSimilarityUtils.GenerateTrigrams(text);

    private double CalculateSimilarity(string source, string target)
        => TextSimilarityUtils.CalculateSimilarity(source, target);
}
