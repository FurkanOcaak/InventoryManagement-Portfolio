using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

namespace WebAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", application = "InventoryManagement API" });
    }
}
