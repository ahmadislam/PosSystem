using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public void Cookie_endpoints_allow_explicit_same_origin_for_swagger_but_reject_hostile_or_missing_origins()
    {
        var configured = new[] { "https://shop.example.com" };
        const string api = "https://api.example.com:8443";

        Assert.True(AuthOriginPolicy.IsAllowed(api, configured, api));
        Assert.True(AuthOriginPolicy.IsAllowed("https://shop.example.com", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("http://api.example.com:8443", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com.attacker.test:8443", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com:8443/other", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com:8443, https://attacker.test", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("https://api.example.com:8443@attacker.test", configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed(null, configured, api));
        Assert.False(AuthOriginPolicy.IsAllowed("null", configured, api));
    }

    [Theory]
    [InlineData("https://api.example.com:8443", 401)]
    [InlineData("https://shop.example.com", 401)]
    [InlineData("https://attacker.test", 403)]
    [InlineData("http://api.example.com:8443", 403)]
    [InlineData(null, 403)]
    public async Task Refresh_controller_uses_its_own_origin_without_accepting_untrusted_requests(string? origin, int expectedStatus)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:Origins:0"] = "https://shop.example.com"
        }).Build();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.com", 8443);
        if (origin is not null) context.Request.Headers["Origin"] = origin;
        var controller = new AuthController(null!, configuration, new LoginAttemptLimiter())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        // No cookie means 401 after the origin policy succeeds; hostile origins fail earlier with 403.
        var response = await controller.Refresh(default);
        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeActionResult>(response.Result).StatusCode);
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
    public async Task Authorization_change_fails_bearer_authentication_for_existing_access_token()
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
        var validator = new AuthorizationStateValidator(db, jwt);
        var events = new AuthorizationStateJwtEvents(validator);
        TokenValidatedContext Context() => new(new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions()) { Principal = principal };

        var valid = Context();
        await events.TokenValidated(valid);
        Assert.Null(valid.Result);
        Assert.True(await validator.IsValidAsync(principal));
        user.Role = PosSystem.Domain.Enums.UserRole.Admin;
        await db.SaveChangesAsync();
        var revoked = Context();
        await events.TokenValidated(revoked);
        Assert.NotNull(revoked.Result?.Failure);
        Assert.False(revoked.Result.Succeeded);
        Assert.False(await validator.IsValidAsync(principal));
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
