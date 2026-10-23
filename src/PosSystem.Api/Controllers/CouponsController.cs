using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Dtos;
using PosSystem.Application.Services;
using PosSystem.Application.Common;
using PosSystem.Domain.Entities;

namespace PosSystem.Api.Controllers;

[ApiController, Route("api/coupons"), Authorize]
public sealed class CouponsController(ICouponService coupons, IAppDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<IReadOnlyList<CouponDto>>> List(CancellationToken ct) =>
        Ok(await coupons.ListAsync(ct));

    [HttpGet("available")]
    public async Task<ActionResult<IReadOnlyList<CouponDto>>> Available(CancellationToken ct) =>
        Ok(await coupons.AvailableAsync(ct));

    [HttpPost, Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CouponDto>> Create(CouponRequest request, CancellationToken ct)
    {
        var coupon = await coupons.CreateAsync(request, ct);
        return CreatedAtAction(nameof(List), new { id = coupon.Id }, coupon);
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CouponDto>> Update(int id, CouponRequest request, CancellationToken ct) =>
        Ok(await coupons.UpdateAsync(id, request, ct));

    [HttpGet("loyalty-rules")]
    public async Task<ActionResult<LoyaltyRulesDto>> LoyaltyRules(CancellationToken ct)
    {
        var values = await db.Settings.AsNoTracking().Where(setting =>
                setting.SettingKey == "LoyaltyEarnPointsPerCurrency" ||
                setting.SettingKey == "LoyaltyRedemptionPointsPerCurrency")
            .ToDictionaryAsync(setting => setting.SettingKey, setting => setting.Value, ct);
        return Ok(ReadRules(values));
    }

    [HttpPut("loyalty-rules"), Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<LoyaltyRulesDto>> SaveLoyaltyRules(LoyaltyRulesRequest request, CancellationToken ct)
    {
        var values = await db.Settings.Where(setting =>
                setting.SettingKey == "LoyaltyEarnPointsPerCurrency" ||
                setting.SettingKey == "LoyaltyRedemptionPointsPerCurrency")
            .ToDictionaryAsync(setting => setting.SettingKey, ct);
        SetRule(values, "LoyaltyEarnPointsPerCurrency", request.EarnPointsPerCurrencyUnit);
        SetRule(values, "LoyaltyRedemptionPointsPerCurrency", request.RedemptionPointsPerCurrencyUnit);
        await db.SaveChangesAsync(ct);
        return Ok(new LoyaltyRulesDto(request.EarnPointsPerCurrencyUnit, request.RedemptionPointsPerCurrencyUnit));
    }

    private static LoyaltyRulesDto ReadRules(IReadOnlyDictionary<string, string> values) =>
        new(ParseRule(values, "LoyaltyEarnPointsPerCurrency", 1),
            ParseRule(values, "LoyaltyRedemptionPointsPerCurrency", 100));

    private static int ParseRule(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw) && int.TryParse(raw, out var value) && value > 0
            ? value
            : values.ContainsKey(key)
                ? throw new InvalidOperationException($"The setting {key} is invalid.")
                : fallback;

    private void SetRule(Dictionary<string, Setting> values, string key, int value)
    {
        if (values.TryGetValue(key, out var setting)) setting.Value = value.ToString();
        else
        {
            var created = new Setting { SettingKey = key, Value = value.ToString() };
            db.Settings.Add(created);
            values.Add(key, created);
        }
    }
}
