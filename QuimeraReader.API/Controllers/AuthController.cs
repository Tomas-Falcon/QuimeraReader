using Microsoft.AspNetCore.Mvc;

namespace QuimeraReader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Mock endpoints to satisfy the Storyteller frontend

    [HttpPost("/api/token")]
    public IActionResult Login()
    {
        // Return a mock token
        return Ok(new 
        { 
            access_token = "mock-token", 
            token_type = "bearer" 
        });
    }

    [HttpGet("/api/validate")]
    public IActionResult ValidateToken()
    {
        // Always valid
        return Ok();
    }

    [HttpGet("/api/statuses")]
    public IActionResult GetStatuses()
    {
        return Ok(new object[] { });
    }
}
