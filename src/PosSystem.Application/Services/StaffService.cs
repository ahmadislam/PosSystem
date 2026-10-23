using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Services;

public interface IStaffService
{
    Task<IReadOnlyList<StaffDto>> ListAsync(CancellationToken ct = default);
    Task<StaffDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default);
    Task<StaffDto> UpdateAsync(int id, UpdateStaffRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(int id, string password, CancellationToken ct = default);
}

public static class StaffAccessPolicy
{
    public static void EnsureCanUpdateSelf(int? currentUserId, int targetUserId, UserRole role, bool isActive)
    {
        if (currentUserId == targetUserId && (role != UserRole.Admin || !isActive))
            throw new BadRequestException("You cannot deactivate or remove your own Admin access.");
    }

    public static void EnsureActiveAdminRemains(bool currentlyActiveAdmin, int activeAdminCount, UserRole requestedRole, bool requestedActive)
    {
        if (currentlyActiveAdmin && (requestedRole != UserRole.Admin || !requestedActive) && activeAdminCount <= 1)
            throw new ConflictException("The last active Admin account cannot be deactivated or demoted.");
    }
}

public sealed class StaffService(
    IAppDbContext db,
    IPasswordHasher<User> hasher,
    ICurrentUser currentUser) : IStaffService
{
    public async Task<IReadOnlyList<StaffDto>> ListAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new StaffDto(u.Id, u.Username, u.Email, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

    public async Task<StaffDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct))
            throw new ConflictException("That username is already in use.");
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
            throw new ConflictException("That email address is already in use.");

        var user = new User
        {
            Username = username,
            Email = email,
            Role = request.Role,
            IsActive = true
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<StaffDto> UpdateAsync(int id, UpdateStaffRequest request, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("Staff account was not found.");
        StaffAccessPolicy.EnsureCanUpdateSelf(currentUser.UserId, user.Id, request.Role, request.IsActive);

        if (user.Role == UserRole.Admin && user.IsActive)
        {
            var activeAdmins = await db.Users.CountAsync(
                u => u.Role == UserRole.Admin && u.IsActive, ct);
            StaffAccessPolicy.EnsureActiveAdminRemains(true, activeAdmins, request.Role, request.IsActive);
        }

        var changedAuthorization = user.Role != request.Role || user.IsActive != request.IsActive;
        user.Role = request.Role;
        user.IsActive = request.IsActive;
        if (changedAuthorization)
            await RevokeRefreshTokensAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(user);
    }

    public async Task ResetPasswordAsync(int id, string password, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("Staff account was not found.");
        user.PasswordHash = hasher.HashPassword(user, password);
        await RevokeRefreshTokensAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task RevokeRefreshTokensAsync(int userId, CancellationToken ct)
    {
        var activeTokens = await db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in activeTokens)
            token.RevokedAt = DateTime.UtcNow;
    }

    private static StaffDto ToDto(User user) =>
        new(user.Id, user.Username, user.Email, user.Role, user.IsActive, user.CreatedAt);
}
