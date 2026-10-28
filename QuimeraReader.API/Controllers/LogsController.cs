using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private static readonly object _fileLock = new();
        private static readonly List<ClientLogDto> _recentLogs = new();
        private const int MaxRecentLogs = 1000;

        public LogsController(ILogger<LogsController> logger)
        {
            _logger = logger;
        }

        private static string GetLogFilePath()
        {
            var dbPath = Environment.GetEnvironmentVariable("QUIMERA_DB_PATH") ?? "quimerareader.db";
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? AppContext.BaseDirectory;
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return Path.Combine(dir, "client-android.log");
        }

        [HttpPost]
        public IActionResult ReceiveLogs([FromBody] List<ClientLogDto> logs)
        {
            if (logs == null || logs.Count == 0) return Ok();

            var sb = new StringBuilder();

            foreach (var log in logs)
            {
                var msg = "[" + log.Level + "] [MAUI] " + log.CreatedAt.ToString("O") + " - " + log.Message;
                if (!string.IsNullOrEmpty(log.Exception))
                {
                    msg += "\nException: " + log.Exception;
                }

                sb.AppendLine(msg);

                switch (log.Level.ToUpperInvariant())
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

            lock (_fileLock)
            {
                foreach (var log in logs)
                {
                    _recentLogs.Add(log);
                }
                if (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.RemoveRange(0, _recentLogs.Count - MaxRecentLogs);
                }

                try
                {
                    var logPath = GetLogFilePath();
                    // Rotar si supera 5 MB
                    if (System.IO.File.Exists(logPath) && new FileInfo(logPath).Length > 5 * 1024 * 1024)
                    {
                        var bakPath = logPath + ".old";
                        if (System.IO.File.Exists(bakPath)) System.IO.File.Delete(bakPath);
                        System.IO.File.Move(logPath, bakPath);
                    }
                    System.IO.File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo persistir el log de Android en disco.");
                }
            }

            return Ok(new { received = logs.Count });
        }

        [HttpGet]
        public IActionResult GetLogs([FromQuery] int limit = 250)
        {
            limit = Math.Clamp(limit, 1, 1000);
            lock (_fileLock)
            {
                var items = _recentLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(limit)
                    .ToList();
                return Ok(new
                {
                    logFilePath = GetLogFilePath(),
                    count = items.Count,
                    logs = items
                });
            }
        }

        [HttpGet("raw")]
        public IActionResult GetRawLogFile()
        {
            lock (_fileLock)
            {
                var logPath = GetLogFilePath();
                if (!System.IO.File.Exists(logPath))
                {
                    return Content("Aún no se han recibido logs del cliente Android.", "text/plain; charset=utf-8");
                }
                var content = System.IO.File.ReadAllText(logPath, Encoding.UTF8);
                return Content(content, "text/plain; charset=utf-8");
            }
        }

        [HttpDelete]
        public IActionResult ClearLogs()
        {
            lock (_fileLock)
            {
                _recentLogs.Clear();
                var logPath = GetLogFilePath();
                if (System.IO.File.Exists(logPath))
                {
                    try { System.IO.File.Delete(logPath); } catch { }
                }
            }
            return Ok(new { message = "Logs de cliente eliminados." });
        }
    }
}
