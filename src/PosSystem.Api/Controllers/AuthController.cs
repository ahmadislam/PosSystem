using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;

namespace PosSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    private const string RefreshCookie = "refreshToken";

    /// <summary>Returns the access token in the body; the refresh token is set as an HttpOnly cookie.</summary>
    [HttpPost("login"), AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        SetCookie(result);
        return Ok(result.Response);
    }

    [HttpPost("refresh"), AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var token) || string.IsNullOrEmpty(token))
            return Unauthorized();
        var result = await auth.RefreshAsync(token, ct);
        SetCookie(result);
        return Ok(result.Response);
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(RefreshCookie, out var token);
        await auth.LogoutAsync(token, ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UtcNow.AddDays(-1)));
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public IActionResult Me() => Ok(new
    {
        Id = User.FindFirst("sub")?.Value,
        Username = User.FindFirst("name")?.Value,
        Role = User.FindFirst("role")?.Value
    });

    private void SetCookie(AuthResult result) =>
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(result.RefreshExpiresAt));

    // Frontend (Vercel) and API (Render) are on different sites, so SameSite=None + Secure is required.
    private static CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, Expires = expires, Path = "/api/auth"
    };
}
