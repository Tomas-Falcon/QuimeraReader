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

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _dbContext.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSetting([FromBody] SystemSetting setting)
    {
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
    public async Task<IActionResult> DownloadWhisperModel()
    {
        string modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
        string targetPath = "ggml-base.bin";

        if (System.IO.File.Exists(targetPath))
        {
            return Ok(new { Message = "El modelo ya está descargado." });
        }

        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            using var stream = await httpClient.GetStreamAsync(modelUrl);
            using var fileStream = new FileStream(targetPath, FileMode.CreateNew);
            await stream.CopyToAsync(fileStream);

            return Ok(new { Message = "Modelo descargado con éxito." });
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Error al descargar el modelo Whisper");
            return StatusCode(500, new { Message = "Error al descargar el modelo: " + ex.Message });
        }
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
    public IActionResult UpdateContainer([FromServices] Microsoft.Extensions.Hosting.IHostApplicationLifetime appLifetime)
    {
        _logger.LogWarning("Se recibió comando de ACTUALIZACIÓN de contenedor desde los ajustes.");
        
        // Ejecutamos en un hilo separado
        _ = Task.Run(async () =>
        {
            bool watchtowerTriggered = false;
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "quimera");
                var response = await httpClient.PostAsync("http://watchtower:8080/v1/update", null);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Actualización solicitada con éxito a Watchtower HTTP API.");
                    watchtowerTriggered = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("No se detectó servicio Watchtower HTTP API ({Message}), intentando script local...", ex.Message);
            }

            try 
            {
                // Si existe un script de actualización (ej. un webhook local o script de watchtower), intentamos ejecutarlo
                if (System.IO.File.Exists("/app/update_container.sh"))
                {
                    var process = new System.Diagnostics.Process()
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "/app/update_container.sh",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    process.Start();
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar script de actualización");
            }

            if (!watchtowerTriggered)
            {
                await Task.Delay(1000);
                appLifetime.StopApplication(); // Esto detiene el contenedor, permitiendo que Watchtower o el host lo reinicie/actualice
            }
        });

        return Ok(new { Message = "Iniciando proceso de actualización y reinicio..." });
    }
}