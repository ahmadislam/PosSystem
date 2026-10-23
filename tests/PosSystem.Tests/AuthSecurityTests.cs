using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PosSystem.Api.Controllers;
using PosSystem.Api.Persistence;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Infrastructure.Services;
using PosSystem.Tests.Support;

namespace PosSystem.Tests;

public class AuthSecurityTests
{
    private const string SigningKey = "a-test-signing-key-with-at-least-32-characters";

    [Fact]
    public void Cookie_endpoints_accept_only_configured_origins()
    {
        var configured = new[] { "https://shop.example.com" };

        Assert.True(AuthOriginPolicy.IsAllowed("https://shop.example.com", configured));
        Assert.False(AuthOriginPolicy.IsAllowed("https://shop.example.com.attacker.test", configured));
        Assert.False(AuthOriginPolicy.IsAllowed("https://shop.example.com/other", configured));
        Assert.False(AuthOriginPolicy.IsAllowed(null, configured));
        Assert.False(AuthOriginPolicy.IsAllowed("null", configured));
    }

    [Fact]
    public void Login_limiter_blocks_repeated_address_and_username_failures()
    {
        var limiter = new LoginAttemptLimiter();

        for (var i = 0; i < 4; i++)
        {
            Assert.Null(limiter.GetRetryAfter("192.0.2.10", "Cashier"));
            limiter.RecordFailure("192.0.2.10", "Cashier");
        }
        Assert.Null(limiter.GetRetryAfter("192.0.2.10", "Cashier"));
        limiter.RecordFailure("192.0.2.10", "Cashier");

        Assert.True(limiter.GetRetryAfter("192.0.2.10", "cashier") > TimeSpan.Zero);
        Assert.Null(limiter.GetRetryAfter("192.0.2.10", "different-user"));
        limiter.RecordSuccess("192.0.2.10", "Cashier");
        Assert.Null(limiter.GetRetryAfter("192.0.2.10", "Cashier"));
    }

    [Fact]
    public void Authorization_stamp_changes_when_password_role_or_status_changes()
    {
        var user = new User { Id = 4, PasswordHash = "hash", Role = PosSystem.Domain.Enums.UserRole.Admin };
        var original = AuthorizationStamp.Create(user, SigningKey);

        Assert.True(AuthorizationStamp.Matches(original, user, SigningKey));
        user.PasswordHash = "new hash";
        Assert.False(AuthorizationStamp.Matches(original, user, SigningKey));
        user.PasswordHash = "hash";
        user.Role = PosSystem.Domain.Enums.UserRole.Manager;
        Assert.False(AuthorizationStamp.Matches(original, user, SigningKey));
        user.Role = PosSystem.Domain.Enums.UserRole.Admin;
        user.IsActive = false;
        Assert.False(AuthorizationStamp.Matches(original, user, SigningKey));
    }

    [Fact]
    public async Task Authorization_change_makes_existing_access_principal_unauthenticated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuthTestDbContext>().UseSqlite(connection).Options;
        await using var db = new AuthTestDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var user = new User { Username = "manager", Email = "manager@example.com", PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var jwt = new JwtSettings { Key = SigningKey };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", user.Id.ToString()),
            new Claim(AuthorizationStamp.ClaimType, AuthorizationStamp.Create(user, jwt.Key))
        ], "Bearer"));
        var transformation = new AuthorizationStateClaimsTransformation(db, jwt);

        Assert.True((await transformation.TransformAsync(principal)).Identity?.IsAuthenticated == true);
        user.Role = PosSystem.Domain.Enums.UserRole.Admin;
        await db.SaveChangesAsync();
        Assert.False((await transformation.TransformAsync(principal)).Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task Refresh_rotation_is_atomic_and_replaying_old_token_revokes_the_new_session()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AuthTestDbContext>().UseSqlite(connection).Options;
        var hasher = new PasswordHasher<User>();
        await using (var setup = new AuthTestDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new User { Username = "cashier", Email = "cashier@example.com" };
            user.PasswordHash = hasher.HashPassword(user, "correct-password");
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        var tokenService = new JwtTokenService(new JwtSettings { Key = SigningKey });
        AuthResult login;
        await using (var loginDb = new AuthTestDbContext(options))
        {
            var auth = new AuthService(loginDb, tokenService, hasher, new JwtSettings { Key = SigningKey });
            login = await auth.LoginAsync(new LoginRequest { Username = "cashier", Password = "correct-password" });
        }
        AuthResult rotated;
        await using (var refreshDb = new AuthTestDbContext(options))
        {
            var auth = new AuthService(refreshDb, tokenService, hasher, new JwtSettings { Key = SigningKey });
            rotated = await auth.RefreshAsync(login.RefreshToken);
        }

        await using var verifyDb = new AuthTestDbContext(options);
        var tokens = await verifyDb.RefreshTokens.OrderBy(token => token.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedByTokenId);
        Assert.Null(tokens[1].RevokedAt);

        await using (var replayDb = new AuthTestDbContext(options))
        {
            var auth = new AuthService(replayDb, tokenService, hasher, new JwtSettings { Key = SigningKey });
            await Assert.ThrowsAsync<UnauthorizedAppException>(() => auth.RefreshAsync(login.RefreshToken));
        }
        await verifyDb.Entry(tokens[1]).ReloadAsync();
        Assert.NotNull(tokens[1].RevokedAt);
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
    }

    [Fact]
    public async Task Concurrent_refresh_requests_cannot_both_keep_an_active_session()
    {
        const string connectionString = "Data Source=concurrent-refresh;Mode=Memory;Cache=Shared;Default Timeout=10";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<AuthTestDbContext>().UseSqlite(connectionString).Options;
        await using (var setup = new AuthTestDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var hasher = new PasswordHasher<User>();
            var user = new User { Username = "cashier", Email = "cashier@example.com" };
            user.PasswordHash = hasher.HashPassword(user, "correct-password");
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        AuthResult login;
        await using (var loginDb = new AuthTestDbContext(options))
        {
            var hasher = new PasswordHasher<User>();
            var service = new AuthService(loginDb, new JwtTokenService(new JwtSettings { Key = SigningKey }),
                hasher, new JwtSettings { Key = SigningKey });
            login = await service.LoginAsync(new LoginRequest { Username = "cashier", Password = "correct-password" });
        }

        async Task<bool> Refresh()
        {
            await using var requestDb = new AuthTestDbContext(options);
            var service = new AuthService(requestDb, new JwtTokenService(new JwtSettings { Key = SigningKey }),
                new PasswordHasher<User>(), new JwtSettings { Key = SigningKey });
            try
            {
                await service.RefreshAsync(login.RefreshToken);
                return true;
            }
            catch (UnauthorizedAppException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(Task.Run(Refresh), Task.Run(Refresh));
        Assert.Single(outcomes, success => success);
        Assert.Single(outcomes, success => !success);

        await using var verifyDb = new AuthTestDbContext(options);
        var tokens = await verifyDb.RefreshTokens.ToListAsync();
        Assert.DoesNotContain(tokens, token => token.RevokedAt is null);
        Assert.Single(tokens, token => token.ReplacedByTokenId is not null);
    }
}
