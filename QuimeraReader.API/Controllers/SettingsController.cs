using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuimeraReader.Infrastructure;
using QuimeraReader.Domain.Entities;
using System.Threading.Tasks;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public SettingsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
            return StatusCode(500, new { Message = "Error al descargar el modelo: " + ex.Message });
        }
    }
}
