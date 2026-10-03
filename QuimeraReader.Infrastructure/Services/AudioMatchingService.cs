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

            var allTitles = await _dbContext.Books.Select(b => new { b.Id, b.Title }).ToListAsync(cancellationToken);
            var contentMatch = allTitles
                .Select(b => new 
                { 
                    Id = b.Id, 
                    Title = b.Title,
                    Hits = (normalizedAudioText.Contains(b.Title.ToLower()) && b.Title.Length > 4) ? 10 : 0
                })
                .Where(x => x.Hits >= 10)
                .OrderByDescending(x => x.Hits)
                .FirstOrDefault();

            if (contentMatch != null)
            {
                _logger.LogInformation("Audio emparejado por CONTENIDO (Título en audio): {File} -> {BookTitle}", Path.GetFileName(audioFilePath), contentMatch.Title);
                return contentMatch.Id;
            }
            
            // 2. Construir un SUBSET de candidatos para evitar extraer el EPUB de toda la biblioteca
            var candidateIds = new HashSet<int>();
            
            if (preCandidateId.HasValue) candidateIds.Add(preCandidateId.Value);

            string rawFileName = Path.GetFileNameWithoutExtension(audioFilePath).ToLowerInvariant();
            var fileWords = rawFileName.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Where(w => w.Length > 4)
                                       .ToList();

            // Subset A: Coincidencia parcial del nombre del archivo (ej. "Harry Potter")
            if (fileWords.Any())
            {
                var partialTitleMatches = await _dbContext.Books
                    .Where(b => !b.AudioTracks.Any())
                    .Where(b => fileWords.Any(w => b.Title.ToLower().Contains(w)))
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                
                foreach(var id in partialTitleMatches) candidateIds.Add(id);
            }

            var whisperWords = normalizedAudioText.Split(new[] { ' ', '.', ',', ':', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Where(w => w.Length > 5)
                                           .Distinct()
                                           .ToList();

            // Subset B: Coincidencia FTS desde el texto de Whisper (Título o Autor mencionado en el audio)
            var topWhisperWords = whisperWords.Take(15).ToList();
            if (topWhisperWords.Any())
            {
                string matchQuery = string.Join(" OR ", topWhisperWords.Select(w => $"\"{w}*\""));
                var ftsMatches = await _dbContext.Books
                    .FromSqlRaw($"SELECT b.* FROM Books b INNER JOIN BooksFTS fts ON b.Id = fts.rowid WHERE BooksFTS MATCH {{0}} LIMIT 20", matchQuery)
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                    
                foreach(var id in ftsMatches) candidateIds.Add(id);
            }

            // Subset C: Fallback a los huérfanos más recientes si fallaron los métodos rápidos
            if (candidateIds.Count == 0)
            {
                var fallbackIds = await _dbContext.Books
                    .Where(b => !b.AudioTracks.Any() && !string.IsNullOrEmpty(b.EpubFilePath))
                    .OrderByDescending(b => b.Id)
                    .Take(100) // Límite de seguridad para evitar cuelgues (O(n))
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                
                foreach(var id in fallbackIds) candidateIds.Add(id);
            }

            // 3. Procesamiento profundo de EPUB SOLO sobre el subset de candidatos
            var candidateBooks = await _dbContext.Books
                .Where(b => candidateIds.Contains(b.Id) && !string.IsNullOrEmpty(b.EpubFilePath))
                .Select(b => new { b.Id, b.Title, b.EpubFilePath })
                .ToListAsync(cancellationToken);

            int bestMatchId = 0;
            int maxHits = 0;

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
                    string epubText = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "<.*?>", string.Empty).ToLowerInvariant();
                    
                    int matchCount = whisperWords.Count(w => epubText.Contains(w));
                    
                    if (matchCount > maxHits)
                    {
                        maxHits = matchCount;
                        bestMatchId = candidate.Id;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error leyendo EPUB para emparejamiento: {Path}", candidate.EpubFilePath);
                }
            }

            if (maxHits > 10 && bestMatchId > 0)
            {
                var matchedTitle = candidateBooks.First(b => b.Id == bestMatchId).Title;
                _logger.LogInformation("Audio emparejado por TEXTO DEL EPUB ({Hits} palabras): {File} -> {BookTitle}", maxHits, Path.GetFileName(audioFilePath), matchedTitle);
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
