using System.Data;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Services;

public interface IShiftService
{
    Task<ShiftDto> OpenAsync(decimal openingCash, CancellationToken ct = default);
    Task<ShiftDto?> GetCurrentAsync(CancellationToken ct = default);
    Task<ShiftDto> BeginCloseAsync(CancellationToken ct = default);
    Task<ShiftDto> CloseAsync(decimal closingCash, CancellationToken ct = default);
}

/// <summary>Shift lifecycle: Open -> PendingClose -> Closed (matches the state diagram).</summary>
public class ShiftService(IAppDbContext db, ICurrentUser current) : IShiftService
{
    private int RequireUser() => current.UserId ?? throw new UnauthorizedAppException("Not signed in.");

    public async Task<ShiftDto> OpenAsync(decimal openingCash, CancellationToken ct = default)
    {
        var userId = RequireUser();
        if (await db.CashierShifts.AnyAsync(s => s.CashierId == userId && s.Status != ShiftStatus.Closed, ct))
            throw new ConflictException("You already have an active shift.");

        var shift = new CashierShift
        {
            CashierId = userId, StartTime = DateTime.UtcNow, OpeningCash = openingCash,
            ExpectedCash = openingCash, Status = ShiftStatus.Open
        };
        db.CashierShifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    public async Task<ShiftDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var userId = RequireUser();
        var shift = await db.CashierShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CashierId == userId && s.Status != ShiftStatus.Closed, ct);
        return shift is null ? null : ToDto(shift);
    }

    public async Task<ShiftDto> BeginCloseAsync(CancellationToken ct = default)
    {
        var userId = RequireUser();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var shiftId = await db.Database
            .SqlQuery<int>($"SELECT \"Id\" AS \"Value\" FROM \"CashierShifts\" WHERE \"CashierId\" = {userId} AND \"Status\" <> {(int)ShiftStatus.Closed} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (shiftId == 0) throw new NotFoundException("No active shift.");
        var shift = await db.CashierShifts.FirstAsync(s => s.Id == shiftId, ct);
        if (shift.Status != ShiftStatus.Open) throw new BadRequestException("Shift is not open.");

        var cashSales = await db.Sales
            .Where(s => s.ShiftId == shift.Id && s.PaymentMethod == PaymentMethod.Cash)
            .SumAsync(s => s.TotalAmount, ct);
        var cashRefunds = await db.SaleReturns
            .Where(r => r.ShiftId == shift.Id && r.RefundMethod == PaymentMethod.Cash)
            .SumAsync(r => r.TotalAmount, ct);
        shift.ExpectedCash = shift.OpeningCash + cashSales - cashRefunds;
        shift.Status = ShiftStatus.PendingClose;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(shift);
    }

    public async Task<ShiftDto> CloseAsync(decimal closingCash, CancellationToken ct = default)
    {
        var shift = await GetActiveAsync(ct);
        if (shift.Status != ShiftStatus.PendingClose) throw new BadRequestException("End the shift first, then enter the closing cash.");

        shift.ClosingCash = closingCash;
        shift.CashVariance = closingCash - shift.ExpectedCash;
        shift.EndTime = DateTime.UtcNow;
        shift.Status = ShiftStatus.Closed;
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    private async Task<CashierShift> GetActiveAsync(CancellationToken ct)
    {
        var userId = RequireUser();
        return await db.CashierShifts.FirstOrDefaultAsync(s => s.CashierId == userId && s.Status != ShiftStatus.Closed, ct)
            ?? throw new NotFoundException("No active shift.");
    }

    private static ShiftDto ToDto(CashierShift s) => new(s.Id, s.CashierId, s.StartTime, s.EndTime, s.OpeningCash,
        s.ExpectedCash, s.ClosingCash, s.CashVariance, s.Status);
}
