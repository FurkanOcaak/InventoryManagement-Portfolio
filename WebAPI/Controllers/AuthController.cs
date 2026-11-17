using Business.Common.Security;
using DataAccess.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPI.Contracts.Auth;
using WebAPI.Infrastructure.Security;

namespace WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser,
    UnknownUserPasswordVerifier unknownUserPasswordVerifier) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationConfiguration.LoginRateLimit)]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            unknownUserPasswordVerifier.Verify(request.Password);
            return LoginFailed();
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? NoContent() : LoginFailed();
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        if (currentUser.UserId is not Guid userId) return Unauthorized();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Unauthorized();
        return Ok(new CurrentUserResponse(userId, user.Email, await userManager.GetRolesAsync(user)));
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfResponse> Csrf([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new CsrfResponse(tokens.RequestToken!, AuthenticationConfiguration.CsrfHeader));
    }

    private ObjectResult LoginFailed() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Giriş başarısız.", detail: "E-posta veya parola hatalı.");
}
