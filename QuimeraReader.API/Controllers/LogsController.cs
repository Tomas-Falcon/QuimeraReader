using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;

namespace QuimeraReader.API.Controllers
{
    public class ClientLogDto
    {
        public int Id { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly ILogger<LogsController> _logger;

        public LogsController(ILogger<LogsController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult ReceiveLogs([FromBody] List<ClientLogDto> logs)
        {
            foreach (var log in logs)
            {
                var msg = "[" + log.Level + "] [MAUI] " + log.CreatedAt.ToString("O") + " - " + log.Message;
                if (!string.IsNullOrEmpty(log.Exception))
                {
                    msg += "\nException: " + log.Exception;
                }

                switch (log.Level.ToUpper())
                {
                    case "ERROR":
                    case "CRITICAL":
                        _logger.LogError(msg);
                        break;
                    case "WARNING":
                        _logger.LogWarning(msg);
                        break;
                    case "DEBUG":
                    case "TRACE":
                        _logger.LogDebug(msg);
                        break;
                    default:
                        _logger.LogInformation(msg);
                        break;
                }
            }
            return Ok();
        }
    }
}
