using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Api.Persistence;

public sealed class AuthorizationStateValidator(IAppDbContext db, JwtSettings jwt)
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        if (principal?.Identity?.IsAuthenticated != true) return false;

        var userId = principal.FindFirst("sub")?.Value;
        var stamp = principal.FindFirst(AuthorizationStamp.ClaimType)?.Value;
        if (!int.TryParse(userId, out var id) || string.IsNullOrEmpty(stamp)) return false;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, ct);
        return user is not null && user.IsActive && AuthorizationStamp.Matches(stamp, user, jwt.Key);
    }
}

/// <summary>Revocation is authentication failure (401), not an authenticated permission denial (403).</summary>
public sealed class AuthorizationStateJwtEvents(AuthorizationStateValidator validator) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        if (!await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted))
            context.Fail("The access token is no longer valid. Please sign in again.");
    }
}
