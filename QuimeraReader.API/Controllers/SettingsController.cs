using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuimeraReader.Infrastructure;
using QuimeraReader.Domain.Entities;
using System.Threading.Tasks;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(AppDbContext dbContext, ILogger<SettingsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // Los secretos nunca salen del servidor: se devuelve una máscara y, si el cliente la reenvía al guardar, se conserva el valor actual
    public const string SecretMask = "••••••••";
    private static readonly HashSet<string> SecretKeys = new() { "HardcoverApiKey", "GoogleBooksApiKey", "OpenAIApiKey" };

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        foreach (var key in SecretKeys)
        {
            if (settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)) settings[key] = SecretMask;
        }
        return Ok(settings);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSetting([FromBody] SystemSetting setting)
    {
        if (SecretKeys.Contains(setting.Key) && setting.Value == SecretMask) return Ok();

        if (setting.Key == "LibraryRootPath" && !string.IsNullOrWhiteSpace(setting.Value))
        {
            if (!Directory.Exists(setting.Value))
            {
                try 
                {
                    Directory.CreateDirectory(setting.Value);
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo crear la carpeta de biblioteca: {Path}", setting.Value);
                    return BadRequest(new { Error = $"No se pudo crear la carpeta: {ex.Message}" });
                }
            }
        }

        var existingSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == setting.Key);
        
        if (existingSetting != null)
        {
            existingSetting.Value = setting.Value;
        }
        else
        {
            _dbContext.SystemSettings.Add(setting);
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }
        [HttpPost("whisper/download")]
    public async Task<IActionResult> DownloadWhisperModel([FromServices] QuimeraReader.Infrastructure.Services.AudioAlignmentQueue queue)
    {
        string modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

        var settingsDict = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        settingsDict.TryGetValue("LibraryRootPath", out var libraryRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot)) libraryRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library");
        
        System.IO.Directory.CreateDirectory(libraryRoot);
        string targetPath = Path.Combine(libraryRoot, "ggml-base.bin");

        // Un modelo de menos de 1 MB es un resto de una descarga fallida de versiones anteriores
        if (System.IO.File.Exists(targetPath) && new FileInfo(targetPath).Length < 1024 * 1024)
        {
            System.IO.File.Delete(targetPath);
        }

        bool wasAlreadyDownloaded = System.IO.File.Exists(targetPath);

        if (!wasAlreadyDownloaded)
        {
            // Descarga atómica: se escribe en un .part y solo se renombra si se completó entera
            string partPath = targetPath + ".part";
            try
            {
                // El modelo pesa ~150 MB: el timeout por defecto de 100 s no basta en conexiones lentas
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                using var response = await httpClient.GetAsync(modelUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();
                long? expectedLength = response.Content.Headers.ContentLength;

                await using (var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted))
                await using (var fileStream = new System.IO.FileStream(partPath, System.IO.FileMode.Create))
                {
                    await stream.CopyToAsync(fileStream, HttpContext.RequestAborted);
                }

                if (expectedLength.HasValue && new FileInfo(partPath).Length != expectedLength.Value)
                {
                    throw new IOException("La descarga del modelo quedó incompleta.");
                }

                System.IO.File.Move(partPath, targetPath, true);
            }
            catch (System.Exception ex)
            {
                try { if (System.IO.File.Exists(partPath)) System.IO.File.Delete(partPath); } catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "No se pudo borrar la descarga parcial {Path}", partPath); }
                _logger.LogError(ex, "Error al descargar el modelo Whisper");
                return StatusCode(500, new { Message = "Error al descargar el modelo: " + ex.Message });
            }
        }

        var modelPathSetting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath");
        if (modelPathSetting == null)
        {
            _dbContext.SystemSettings.Add(new QuimeraReader.Domain.Entities.SystemSetting { Key = "WhisperModelPath", Value = targetPath });
        }
        else
        {
            modelPathSetting.Value = targetPath;
        }

        // Auto-requeue any ERROR books
        var failedBooks = _dbContext.Books.Where(b => b.ProcessingStatus == "ERROR").ToList();
        foreach (var book in failedBooks)
        {
            book.ProcessingStatus = "PENDING_SYNC";
            await queue.EnqueueAsync(book.Id);
        }
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = "Modelo descargado con éxito y trabajos reanudados." });
    }

    [HttpGet("whisper/status")]
    public async Task<IActionResult> GetWhisperStatus()
    {
        var settings = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "WhisperModelPath");
        bool isDownloaded = false;
        
        if (settings != null && !string.IsNullOrEmpty(settings.Value))
        {
            isDownloaded = System.IO.File.Exists(settings.Value);
        }
        else
        {
            isDownloaded = System.IO.File.Exists("ggml-base.bin");
        }
        
        return Ok(new { IsDownloaded = isDownloaded });
    }
[HttpPost("restart")]
    public IActionResult RestartServer([FromServices] Microsoft.Extensions.Hosting.IHostApplicationLifetime appLifetime)
    {
        _logger.LogWarning("Se recibió comando de REINICIO desde los ajustes. Deteniendo la aplicación...");
        
        // Ejecutamos en un hilo separado para permitir que la respuesta HTTP termine y llegue al cliente
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            appLifetime.StopApplication();
        });

        return Ok(new { Message = "Reiniciando servidor..." });
    }
    [HttpPost("update-container")]
    public IActionResult UpdateContainer([FromServices] Microsoft.Extensions.Hosting.IHostApplicationLifetime appLifetime, [FromServices] IConfiguration configuration)
    {
        _logger.LogWarning("Se recibió comando de ACTUALIZACIÓN de contenedor desde los ajustes.");

        // Token de la API HTTP de Watchtower (--http-api-token). "quimera" se mantiene solo por compatibilidad con el compose antiguo.
        var watchtowerToken = configuration["WATCHTOWER_TOKEN"];
        if (string.IsNullOrWhiteSpace(watchtowerToken))
        {
            _logger.LogWarning("WATCHTOWER_TOKEN no está definida; se usa el token por defecto. Defínela en docker-compose.yml.");
            watchtowerToken = "quimera";
        }
        var watchtowerUrl = configuration["WATCHTOWER_URL"] ?? "http://watchtower:8080";

        // Ejecutamos en un hilo separado para que la respuesta HTTP llegue al cliente antes del reinicio
        _ = Task.Run(async () =>
        {
            bool watchtowerTriggered = false;
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", watchtowerToken);
                var response = await httpClient.PostAsync($"{watchtowerUrl.TrimEnd('/')}/v1/update", null);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Actualización solicitada con éxito a Watchtower HTTP API.");
                    watchtowerTriggered = true;
                }
                else
                {
                    _logger.LogWarning("Watchtower respondió {Status} a la petición de actualización.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("No se detectó servicio Watchtower HTTP API ({Message}).", ex.Message);
            }

            if (!watchtowerTriggered)
            {
                await Task.Delay(1000);
                appLifetime.StopApplication(); // Detiene el contenedor; restart: unless-stopped lo vuelve a levantar
            }
        });

        return Ok(new { Message = "Iniciando proceso de actualización y reinicio..." });
    }
}
