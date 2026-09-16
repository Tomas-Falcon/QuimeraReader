using System;
using System.Collections.Generic;
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

        try
        {
            var segments = new List<SyncSegment>();

            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            // NOTA IMPORTANTE: Whisper.net procesa nativamente streams PCM 16-bit 16kHz WAV. 
            // Si el archivo es MP3 o M4B, es necesario convertirlo primero con FFmpeg. 
            // Para mantener la simplicidad y bajo la asunción de que se pasará un WAV o se 
            // implementará un wrapper de FFmpeg, abrimos el FileStream directamente.
            using var fileStream = File.OpenRead(audioFilePath);
            
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                segments.Add(new SyncSegment
                {
                    Text = result.Text,
                    Start = result.Start.TotalSeconds,
                    End = result.End.TotalSeconds
                });
            }

            // Aquí se podría implementar lógica adicional para alinear estrictamente 
            // 'segments' con el 'textContent' del EPUB usando algoritmos como Needleman-Wunsch o DTW.
            // Por ahora, Whisper ya nos da el texto escuchado con timestamps, lo cual sirve de SyncMap inicial.

            string syncMapJson = JsonSerializer.Serialize(segments);

            return new SyncMapResult 
            { 
                Success = true, 
                SyncMapJson = syncMapJson 
            };
        }
        catch (Exception ex)
        {
            return new SyncMapResult { Success = false, ErrorMessage = $"Error durante el procesamiento Whisper: {ex.Message}" };
        }
    }
}

public class SyncMapResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SyncMapJson { get; set; }
}

public class SyncSegment
{
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
}
