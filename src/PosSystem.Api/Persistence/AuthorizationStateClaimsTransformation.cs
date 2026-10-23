using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Infrastructure.Services;

namespace PosSystem.Api.Persistence;

public sealed class AuthorizationStateClaimsTransformation(IAppDbContext db, JwtSettings jwt) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity;
        if (identity?.IsAuthenticated != true) return principal;

        var userId = principal.FindFirst("sub")?.Value;
        var stamp = principal.FindFirst(AuthorizationStamp.ClaimType)?.Value;
        if (!int.TryParse(userId, out var id) || string.IsNullOrEmpty(stamp))
            return UnauthenticatedPrincipal();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id);
        return user is not null && user.IsActive && AuthorizationStamp.Matches(stamp, user, jwt.Key)
            ? principal
            : UnauthenticatedPrincipal();
    }

    private static ClaimsPrincipal UnauthenticatedPrincipal() => new(new ClaimsIdentity());
}
