using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public async Task<SyncMapResult> GenerateSyncMapAsync(string audioFilePath, string textContent)
    {
        var modelPathSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath");
        string modelPath = modelPathSetting?.Value ?? "ggml-base.bin"; // Fallback a local en la raíz

        if (!File.Exists(modelPath))
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"El modelo de Whisper no fue encontrado en la ruta: {modelPath}. Por favor configúrelo en Settings." };
        }

        if (!File.Exists(audioFilePath))
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"El archivo de audio no existe: {audioFilePath}" };
        }

        string tempWavFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_full.wav");

        try
        {
            var segments = new List<SyncSegment>();

            // Whisper.net procesa nativamente streams PCM 16-bit 16kHz WAV, por lo que
            // convertimos primero el audio (MP3, M4B, etc.) con FFmpeg.
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

                // Hay que drenar los streams redirigidos para que FFmpeg no se bloquee.
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
            }

            if (!File.Exists(tempWavFile) || new FileInfo(tempWavFile).Length < 1024)
            {
                return new SyncMapResult { Success = false, ErrorMessage = "FFmpeg no pudo convertir el audio (archivo corrupto o formato no soportado)." };
            }

            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            using var fileStream = File.OpenRead(tempWavFile);

            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                segments.Add(new SyncSegment
                {
                    Text = result.Text,
                    Start = result.Start.TotalSeconds,
                    End = result.End.TotalSeconds
                });
            }

            // Whisper nos da el texto escuchado con timestamps, lo cual sirve de SyncMap inicial.
            // Antes de darlo por bueno, medimos cuánto de lo transcrito aparece realmente en el EPUB:
            // si el audio es de otro libro, la cobertura será muy baja.
            double coverage = ComputeTrigramCoverage(segments.Select(s => s.Text), textContent);

            string syncMapJson = JsonSerializer.Serialize(segments);

            return new SyncMapResult 
            { 
                Success = true, 
                SyncMapJson = syncMapJson,
                Coverage = coverage
            };
        }
        catch (Exception ex)
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"Error durante el procesamiento Whisper: {ex.Message}" };
        }
        finally
        {
            // Limpiar wav temporal
            if (File.Exists(tempWavFile)) try { File.Delete(tempWavFile); } catch { }
        }
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

public class SyncMapResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SyncMapJson { get; set; }
    public double Coverage { get; set; }
}

public class SyncSegment
{
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
}
