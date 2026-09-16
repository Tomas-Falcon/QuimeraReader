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
}
