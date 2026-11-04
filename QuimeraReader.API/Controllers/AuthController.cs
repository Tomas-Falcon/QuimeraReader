using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuimeraReader.API.Security;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApiKeyAuth _auth;

    public AuthController(ApiKeyAuth auth)
    {
        _auth = auth;
    }

    public class LoginRequest
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { AuthRequired = _auth.IsEnabled, Authenticated = _auth.IsAuthenticated(HttpContext) });
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (!_auth.IsEnabled) return Ok(new { AuthRequired = false });
        if (!_auth.IsValidKey(request.ApiKey)) return Unauthorized(new { error = "Clave de acceso incorrecta." });

        // Cookie HttpOnly para el cliente web (mismo origen): cubre también <img>, <audio> y epub.js
        Response.Cookies.Append(ApiKeyAuth.CookieName, _auth.CookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            Path = "/"
        });
        return Ok(new { AuthRequired = true });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(ApiKeyAuth.CookieName, new CookieOptions { Path = "/" });
        return Ok();
    }
}
