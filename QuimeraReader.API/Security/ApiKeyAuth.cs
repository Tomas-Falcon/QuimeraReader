using System.Security.Cryptography;
using System.Text;

namespace QuimeraReader.API.Security;

/// <summary>
/// Autenticación mínima para homelab: una única API key definida en la variable de entorno QUIMERA_API_KEY.
/// Si la variable no está definida la API queda abierta (compatibilidad con instalaciones existentes) y se avisa en el log.
/// </summary>
public class ApiKeyAuth
{
    public const string HeaderName = "X-Api-Key";
    public const string CookieName = "quimera_auth";
    // Query string para recursos que el navegador/WebView carga directamente (<img>, <audio>, epub.js) desde otro origen (MAUI/Desktop)
    public const string QueryName = "access_token";

    private readonly byte[]? _keyBytes;
    private readonly string? _cookieValue;

    public ApiKeyAuth(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        _keyBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        // La cookie no guarda la clave en claro: guarda un HMAC derivado de ella
        _cookieValue = Convert.ToHexString(HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes("quimera-cookie-v1")));
    }

    public bool IsEnabled => _keyBytes != null;

    public string CookieValue => _cookieValue ?? string.Empty;

    public bool IsValidKey(string? candidate)
    {
        if (_keyBytes == null || string.IsNullOrEmpty(candidate)) return false;
        return CryptographicOperations.FixedTimeEquals(_keyBytes, Encoding.UTF8.GetBytes(candidate.Trim()));
    }

    public bool IsAuthenticated(HttpContext context)
    {
        if (!IsEnabled) return true;

        if (IsValidKey(context.Request.Headers[HeaderName].FirstOrDefault())) return true;
        if (IsValidKey(context.Request.Query[QueryName].FirstOrDefault())) return true;

        var cookie = context.Request.Cookies[CookieName];
        return !string.IsNullOrEmpty(cookie) &&
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(_cookieValue!));
    }
}

public class ApiKeyAuthMiddleware
{
    private static readonly string[] PublicApiPaths =
    {
        "/api/health",
        "/api/auth/status",
        "/api/auth/login",
        "/api/auth/logout"
    };

    private readonly RequestDelegate _next;
    private readonly ApiKeyAuth _auth;

    public ApiKeyAuthMiddleware(RequestDelegate next, ApiKeyAuth auth)
    {
        _next = next;
        _auth = auth;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // Los archivos estáticos del cliente WASM (fuera de /api) siempre son públicos: la propia app muestra el login
        bool isApi = path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        bool isPublic = PublicApiPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        if (!isApi || isPublic || HttpMethods.IsOptions(context.Request.Method) || _auth.IsAuthenticated(context))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "No autorizado." });
    }
}
