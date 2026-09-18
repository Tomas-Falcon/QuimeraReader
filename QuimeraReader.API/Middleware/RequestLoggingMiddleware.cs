using System.Diagnostics;

namespace QuimeraReader.API.Middleware;

/// <summary>
/// Middleware que loguea cada petición HTTP: método, ruta, código de respuesta y duración.
/// Nivel Information para 2xx, Warning para 4xx, Error para 5xx.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // No loguear peticiones de archivos estáticos (JS, CSS, imágenes, wasm, etc.)
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();

        var statusCode = context.Response.StatusCode;
        var method = context.Request.Method;
        var elapsed = stopwatch.ElapsedMilliseconds;

        if (statusCode >= 500)
        {
            _logger.LogError(
                "HTTP {Method} {Path} respondió {StatusCode} en {ElapsedMs}ms",
                method, path, statusCode, elapsed);
        }
        else if (statusCode >= 400)
        {
            _logger.LogWarning(
                "HTTP {Method} {Path} respondió {StatusCode} en {ElapsedMs}ms",
                method, path, statusCode, elapsed);
        }
        else
        {
            _logger.LogInformation(
                "HTTP {Method} {Path} respondió {StatusCode} en {ElapsedMs}ms",
                method, path, statusCode, elapsed);
        }
    }
}
