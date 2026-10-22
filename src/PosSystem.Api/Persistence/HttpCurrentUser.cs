using PosSystem.Application.Common;

namespace PosSystem.Api.Persistence;

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId => int.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;
    public string? Role => Principal?.FindFirst("role")?.Value;
}
