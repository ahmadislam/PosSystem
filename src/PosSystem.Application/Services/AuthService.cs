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
    Task<AuthResult> LoginDemoAsync(string username, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);
}

public class AuthService(
    IAppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> hasher,
    JwtSettings jwt,
    DemoAccessSettings? demoAccess = null) : IAuthService
{
    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive, ct);
        if (user is null ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            throw new UnauthorizedAppException("Invalid username or password.");

        var issued = await IssueAsync(user, ct);
        return issued.Result;
    }

    public async Task<AuthResult> LoginDemoAsync(string username, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            candidate => candidate.Username == username && candidate.IsActive,
            ct);
        if (user is null)
            throw new UnauthorizedAppException("The portfolio demo is temporarily unavailable.");

        var issued = await IssueAsync(user, ct);
        return issued.Result;
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashToken(refreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) throw new UnauthorizedAppException("Invalid refresh token.");

        if (existing.RevokedAt is not null)
        {
            if (existing.ReplacedByTokenId is not null)
            {
                await RevokeActiveTokensAsync(existing.UserId, DateTime.UtcNow, ct);
                await transaction.CommitAsync(ct);
            }
            throw new UnauthorizedAppException("Refresh token was already used. Please sign in again.");
        }

        if (existing.ExpiresAt <= DateTime.UtcNow || !existing.User.IsActive)
            throw new UnauthorizedAppException("Refresh token expired.");

        var now = DateTime.UtcNow;
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == existing.Id && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.RevokedAt, now), ct);

        if (claimed != 1)
        {
            await RevokeActiveTokensAsync(existing.UserId, now, ct);
            await transaction.CommitAsync(ct);
            throw new UnauthorizedAppException("Refresh token was already used. Please sign in again.");
        }

        var issued = await IssueAsync(existing.User, ct);
        await db.RefreshTokens
            .Where(t => t.Id == existing.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(t => t.ReplacedByTokenId, (int?)issued.RefreshTokenId), ct);
        await transaction.CommitAsync(ct);
        return issued.Result;
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

    private async Task<(AuthResult Result, int RefreshTokenId)> IssueAsync(User user, CancellationToken ct)
    {
        var raw = tokens.GenerateRefreshToken();
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(jwt.RefreshTokenDays)
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user, out var expires);
        return (
            new AuthResult(new AuthResponse(
                access,
                expires,
                user.Username,
                user.Role.ToString(),
                demoAccess?.Enabled == true && string.Equals(
                    user.Username,
                    demoAccess.Username,
                    StringComparison.OrdinalIgnoreCase)), raw, entity.ExpiresAt),
            entity.Id);
    }

    private Task<int> RevokeActiveTokensAsync(int userId, DateTime revokedAt, CancellationToken ct) =>
        db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, revokedAt), ct);
}
