using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Domain.Entities;
using PosSystem.Infrastructure.Services;

namespace PosSystem.IntegrationTests;

public sealed class AuthenticationDatabaseTests
{
    [PgFact]
    public async Task Concurrent_refresh_consumes_token_once_and_replay_revokes_successor()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        await database.SeedAsync();
        var jwt = new JwtSettings { Key = "integration-signing-key-that-is-longer-than-thirty-two-characters" };
        var tokens = new JwtTokenService(jwt);
        AuthService Auth(PosSystem.Infrastructure.Persistence.AppDbContext db) =>
            new(db, tokens, new PasswordHasher<User>(), jwt);
        AuthResult login;
        await using (var db = database.Open()) login = await Auth(db).LoginAsync(new LoginRequest
        {
            Username = "integration-admin", Password = ApiProcess.Password
        });
        var barrier = new RefreshReadBarrier();
        async Task<bool> RefreshAsync()
        {
            await using var db = database.Open(barrier);
            try
            {
                await Auth(db).RefreshAsync(login.RefreshToken);
                return true;
            }
            catch (UnauthorizedAppException) { return false; }
        }
        var results = await Task.WhenAll(RefreshAsync(), RefreshAsync()).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, success => success);
        await using var verify = database.Open();
        Assert.Equal(2, await verify.RefreshTokens.CountAsync());
        Assert.Empty(await verify.RefreshTokens.Where(token => token.RevokedAt == null).ToListAsync());
    }

    private sealed class RefreshReadBarrier : DbCommandInterceptor
    {
        private int readers;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"RefreshTokens\"") && command.CommandText.Contains("JOIN"))
            {
                if (Interlocked.Increment(ref readers) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
