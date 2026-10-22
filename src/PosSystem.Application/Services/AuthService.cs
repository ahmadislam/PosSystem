using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public record AuthResult(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);
}

public class AuthService(IAppDbContext db, ITokenService tokens, IPasswordHasher<User> hasher, JwtSettings jwt) : IAuthService
{
    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive, ct);
        if (user is null ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            throw new UnauthorizedAppException("Invalid username or password.");

        return await IssueAsync(user, null, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var existing = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) throw new UnauthorizedAppException("Invalid refresh token.");

        if (existing.RevokedAt is not null)
        {
            // A rotated token was presented again: assume it leaked and revoke every active token of this user.
            var active = await db.RefreshTokens.Where(t => t.UserId == existing.UserId && t.RevokedAt == null).ToListAsync(ct);
            foreach (var t in active) t.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedAppException("Refresh token was already used. Please sign in again.");
        }

        if (existing.ExpiresAt <= DateTime.UtcNow || !existing.User.IsActive)
            throw new UnauthorizedAppException("Refresh token expired.");

        return await IssueAsync(existing.User, existing, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = tokens.HashToken(refreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (existing is null) return;
        existing.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResult> IssueAsync(User user, RefreshToken? replaced, CancellationToken ct)
    {
        var raw = tokens.GenerateRefreshToken();
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(jwt.RefreshTokenDays)
        };
        db.RefreshTokens.Add(entity);
        if (replaced is not null) replaced.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (replaced is not null)
        {
            replaced.ReplacedByTokenId = entity.Id;
            await db.SaveChangesAsync(ct);
        }

        var access = tokens.CreateAccessToken(user, out var expires);
        return new AuthResult(new AuthResponse(access, expires, user.Username, user.Role.ToString()), raw, entity.ExpiresAt);
    }
}
