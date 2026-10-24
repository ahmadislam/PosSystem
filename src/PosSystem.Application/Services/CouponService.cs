using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public interface ICouponService
{
    Task<IReadOnlyList<CouponDto>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CouponDto>> AvailableAsync(CancellationToken ct = default);
    Task<CouponDto> CreateAsync(CouponRequest request, CancellationToken ct = default);
    Task<CouponDto> UpdateAsync(int id, CouponRequest request, CancellationToken ct = default);
}

public sealed class CouponService(IAppDbContext db) : ICouponService
{
    public async Task<IReadOnlyList<CouponDto>> ListAsync(CancellationToken ct = default) =>
        (await db.Coupons.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<IReadOnlyList<CouponDto>> AvailableAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return (await db.Coupons.AsNoTracking()
            .Where(c => c.IsActive && c.StartsAt <= now && c.EndsAt > now &&
                (!c.MaxRedemptions.HasValue || c.RedemptionCount < c.MaxRedemptions.Value))
            .OrderBy(c => c.Code).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<CouponDto> CreateAsync(CouponRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct))
            throw new ConflictException("Coupon code already exists.");
        var coupon = new Coupon();
        Apply(coupon, request, code);
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return ToDto(coupon);
    }

    public async Task<CouponDto> UpdateAsync(int id, CouponRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Coupon not found.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Id != id && c.Code == code, ct))
            throw new ConflictException("Coupon code already exists.");
        Apply(coupon, request, code);
        await db.SaveChangesAsync(ct);
        return ToDto(coupon);
    }

    private static void Validate(CouponRequest request)
    {
        if (request.IsPercentage && request.Value > 100)
            throw new BadRequestException("Percentage coupons cannot exceed 100%.");
        if (request.EndsAt <= request.StartsAt)
            throw new BadRequestException("Coupon end time must be after its start time.");
        if (request.StartsAt.Kind != DateTimeKind.Utc || request.EndsAt.Kind != DateTimeKind.Utc)
            throw new BadRequestException("Coupon validity timestamps must be UTC.");
    }

    private static void Apply(Coupon coupon, CouponRequest request, string code)
    {
        coupon.Code = code;
        coupon.Name = request.Name.Trim();
        coupon.IsPercentage = request.IsPercentage;
        coupon.Value = request.Value;
        coupon.StartsAt = request.StartsAt;
        coupon.EndsAt = request.EndsAt;
        coupon.IsActive = request.IsActive;
        coupon.MaxRedemptions = request.MaxRedemptions;
    }

    private static CouponDto ToDto(Coupon c) => new(c.Id, c.Code, c.Name, c.IsPercentage, c.Value,
        c.StartsAt, c.EndsAt, c.IsActive, c.MaxRedemptions, c.RedemptionCount);
}
