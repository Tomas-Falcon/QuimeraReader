using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Whisper.net;
using QuimeraReader.Domain.Entities;

namespace QuimeraReader.Infrastructure.Services;

public class AudioAlignmentService
{
    private readonly AppDbContext _dbContext;

    public AudioAlignmentService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SyncMapResult> GenerateSyncMapAsync(string audioFilePath, string textContent, CancellationToken cancellationToken = default)
    {
        var singleTrackList = new List<(int TrackNumber, string FilePath)> { (1, audioFilePath) };
        return await GenerateMultiTrackSyncMapAsync(singleTrackList, textContent, null, cancellationToken);
    }

    public async Task<SyncMapResult> GenerateMultiTrackSyncMapAsync(
        IReadOnlyList<(int TrackNumber, string FilePath)> tracks,
        string fullTextContent,
        IReadOnlyList<EpubSpineChapter>? spineChapters = null,
        CancellationToken cancellationToken = default)
    {
        var modelPathSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath", cancellationToken);
        string modelPath = modelPathSetting?.Value ?? "ggml-base.bin";

        if (!File.Exists(modelPath))
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"El modelo de Whisper no fue encontrado en la ruta: {modelPath}. Por favor configúrelo en Settings." };
        }

        var validTracks = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
            .OrderBy(t => t.TrackNumber)
            .ToList();

        if (validTracks.Count == 0)
        {
            return new SyncMapResult { Success = false, ErrorMessage = "Ninguno de los archivos de audio existe en disco." };
        }

        try
        {
            var allSegments = new List<SyncSegment>();
            using var factory = WhisperFactory.FromPath(modelPath);

            // Precalcular trigramas por capítulo del EPUB para anclar cada segmento a su SpineIndex / ChapterHref
            var chapterProfiles = BuildChapterTrigramProfiles(spineChapters);
            int currentHintSpineIndex = 0;

            foreach (var (trackNumber, audioFilePath) in validTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempWavFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_t{trackNumber}.wav");

                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{audioFilePath}\" -ar 16000 -ac 1 -c:a pcm_s16le -y \"{tempWavFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null)
                            return new SyncMapResult { Success = false, ErrorMessage = "No se pudo iniciar FFmpeg." };

                        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                        await process.WaitForExitAsync(cancellationToken);
                        await Task.WhenAll(stdOutTask, stdErrTask);
                    }

                    if (!File.Exists(tempWavFile) || new FileInfo(tempWavFile).Length < 1024)
                    {
                        return new SyncMapResult { Success = false, ErrorMessage = $"FFmpeg no pudo convertir la pista {trackNumber} (archivo corrupto o formato no soportado)." };
                    }

                    using var processor = factory.CreateBuilder()
                        .WithLanguage("auto")
                        .Build();

                    using var fileStream = File.OpenRead(tempWavFile);

                    await foreach (var result in processor.ProcessAsync(fileStream).WithCancellation(cancellationToken))
                    {
                        var segText = (result.Text ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(segText)) continue;

                        var seg = new SyncSegment
                        {
                            TrackNumber = trackNumber,
                            Text = segText,
                            Start = result.Start.TotalSeconds,
                            End = result.End.TotalSeconds
                        };

                        if (chapterProfiles.Count > 0)
                        {
                            var matchedChapter = MatchSegmentToChapter(segText, chapterProfiles, ref currentHintSpineIndex);
                            if (matchedChapter != null)
                            {
                                seg.SpineIndex = matchedChapter.SpineIndex;
                                seg.ChapterHref = matchedChapter.ChapterHref;
                            }
                            else
                            {
                                seg.SpineIndex = currentHintSpineIndex;
                                seg.ChapterHref = chapterProfiles[Math.Clamp(currentHintSpineIndex, 0, chapterProfiles.Count - 1)].ChapterHref;
                            }
                        }

                        allSegments.Add(seg);
                    }
                }
                finally
                {
                    if (File.Exists(tempWavFile))
                    {
                        try { File.Delete(tempWavFile); } catch { }
                    }
                }
            }

            double coverage = ComputeTrigramCoverage(allSegments.Select(s => s.Text), fullTextContent);
            string syncMapJson = JsonSerializer.Serialize(allSegments);

            return new SyncMapResult
            {
                Success = true,
                SyncMapJson = syncMapJson,
                Coverage = coverage
            };
        }
        catch (OperationCanceledException)
        {
            return new SyncMapResult { Success = false, ErrorMessage = "Procesamiento cancelado por apagado del servidor." };
        }
        catch (Exception ex)
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"Error durante el procesamiento Whisper: {ex.Message}" };
        }
    }

    private sealed class ChapterTrigramProfile
    {
        public int SpineIndex { get; init; }
        public string ChapterHref { get; init; } = string.Empty;
        public HashSet<string> Trigrams { get; init; } = new();
        public string NormalizedText { get; init; } = string.Empty;
    }

    private static List<ChapterTrigramProfile> BuildChapterTrigramProfiles(IReadOnlyList<EpubSpineChapter>? chapters)
    {
        var list = new List<ChapterTrigramProfile>();
        if (chapters == null || chapters.Count == 0) return list;

        foreach (var ch in chapters)
        {
            var trigrams = BuildTrigrams(ch.PlainText);
            var norm = NormalizeWords(ch.PlainText);
            list.Add(new ChapterTrigramProfile
            {
                SpineIndex = ch.SpineIndex,
                ChapterHref = ch.ChapterHref,
                Trigrams = trigrams,
                NormalizedText = norm
            });
        }
        return list;
    }

    private static ChapterTrigramProfile? MatchSegmentToChapter(string segmentText, List<ChapterTrigramProfile> profiles, ref int currentHintSpineIndex)
    {
        var segTrigrams = EnumerateTrigrams(segmentText).ToList();
        if (segTrigrams.Count == 0)
        {
            return null;
        }

        ChapterTrigramProfile? bestProfile = null;
        double bestScore = 0;

        for (int i = 0; i < profiles.Count; i++)
        {
            var prof = profiles[i];
            if (prof.Trigrams.Count == 0) continue;

            int hits = 0;
            foreach (var tri in segTrigrams)
            {
                if (prof.Trigrams.Contains(tri)) hits++;
            }

            if (hits > 0)
            {
                // Bonificar ligeramente capítulos cercanos al capítulo actual o el siguiente para mantener orden secuencial
                double proximityBonus = (i == currentHintSpineIndex || i == currentHintSpineIndex + 1) ? 0.5 : 0.0;
                double score = hits + proximityBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestProfile = prof;
                }
            }
        }

        if (bestProfile != null && bestScore >= 1.5)
        {
            currentHintSpineIndex = bestProfile.SpineIndex;
            return bestProfile;
        }

        return null;
    }

    private static string NormalizeWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decoded = System.Net.WebUtility.HtmlDecode(text);
        var clean = new string(decoded
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray());
        return string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Fracción (0..1) de los trigramas de palabras de la transcripción que existen en el texto del EPUB.
    /// Tolera errores puntuales de Whisper: una palabra mal transcrita solo rompe 3 trigramas.
    /// </summary>
    public static double ComputeTrigramCoverage(IEnumerable<string> transcriptSegments, string epubText)
    {
        var epubTrigrams = BuildTrigrams(epubText);
        if (epubTrigrams.Count == 0) return 0;

        int total = 0, hits = 0;
        foreach (var segment in transcriptSegments)
        {
            foreach (var trigram in EnumerateTrigrams(segment))
            {
                total++;
                if (epubTrigrams.Contains(trigram)) hits++;
            }
        }

        return total == 0 ? 0 : (double)hits / total;
    }

    private static HashSet<string> BuildTrigrams(string text)
    {
        var set = new HashSet<string>();
        foreach (var t in EnumerateTrigrams(text)) set.Add(t);
        return set;
    }

    private static IEnumerable<string> EnumerateTrigrams(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var decoded = System.Net.WebUtility.HtmlDecode(text);
        var clean = new string(decoded
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray());
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length - 2; i++)
        {
            yield return words[i] + " " + words[i + 1] + " " + words[i + 2];
        }
    }
}

public class EpubSpineChapter
{
    public int SpineIndex { get; set; }
    public string ChapterHref { get; set; } = string.Empty;
    public string PlainText { get; set; } = string.Empty;
}

public class SyncMapResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SyncMapJson { get; set; }
    public double Coverage { get; set; }
}

public class SyncSegment
{
    [JsonPropertyName("trackNumber")]
    public int TrackNumber { get; set; } = 1;

    [JsonPropertyName("spineIndex")]
    public int? SpineIndex { get; set; }

    [JsonPropertyName("chapterHref")]
    public string? ChapterHref { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }
}
