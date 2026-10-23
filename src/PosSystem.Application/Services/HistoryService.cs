using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Services;

public interface IExcelReportService
{
    Task<byte[]> CreateSalesReportAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<byte[]> CreateInventoryReportAsync(CancellationToken ct = default);
}

public interface IHistoryService
{
    Task<PagedResult<AuditLogDto>> ListAuditAsync(
        DateTime? from, DateTime? to, string? entityName, AuditAction? action,
        int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<StockMovementDto>> ListStockMovementsAsync(
        int? productId, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken ct = default);
}

public class HistoryService(IAppDbContext db) : IHistoryService
{
    public async Task<PagedResult<AuditLogDto>> ListAuditAsync(
        DateTime? from, DateTime? to, string? entityName, AuditAction? action,
        int page, int pageSize, CancellationToken ct = default)
    {
        ValidateDates(from, to);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query =
            from audit in db.AuditLogs.AsNoTracking()
            join user in db.Users.IgnoreQueryFilters().AsNoTracking()
                on audit.UserId equals (int?)user.Id into userJoin
            from user in userJoin.DefaultIfEmpty()
            select new { audit, Username = user == null ? null : user.Username };

        if (from.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.audit.Timestamp >= start);
        }
        if (to.HasValue)
        {
            var end = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.audit.Timestamp < end);
        }
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            var entity = entityName.Trim();
            query = query.Where(x => x.audit.EntityName == entity);
        }
        if (action.HasValue) query = query.Where(x => x.audit.Action == action.Value);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.audit.Timestamp)
            .ThenByDescending(x => x.audit.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogDto(
                x.audit.Id, x.audit.Timestamp, x.audit.UserId, x.Username, x.audit.Action,
                x.audit.EntityName, x.audit.EntityId, x.audit.OldValues, x.audit.NewValues))
            .ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<StockMovementDto>> ListStockMovementsAsync(
        int? productId, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken ct = default)
    {
        ValidateDates(from, to);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query =
            from movement in db.StockMovements.AsNoTracking()
            join product in db.Products.IgnoreQueryFilters().AsNoTracking()
                on movement.ProductId equals product.Id
            join user in db.Users.IgnoreQueryFilters().AsNoTracking()
                on movement.CreatedByUserId equals user.Id into userJoin
            from user in userJoin.DefaultIfEmpty()
            select new { movement, product.Sku, product.Name, Username = user == null ? null : user.Username };

        if (productId.HasValue) query = query.Where(x => x.movement.ProductId == productId.Value);
        if (from.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.movement.CreatedAt >= start);
        }
        if (to.HasValue)
        {
            var end = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.movement.CreatedAt < end);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.movement.CreatedAt)
            .ThenByDescending(x => x.movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StockMovementDto(
                x.movement.Id, x.movement.CreatedAt, x.movement.ProductId, x.Sku, x.Name,
                x.movement.CreatedByUserId, x.Username, x.movement.Type, x.movement.QuantityChange,
                x.movement.ReferenceType, x.movement.ReferenceId, x.movement.Note))
            .ToListAsync(ct);
        return new PagedResult<StockMovementDto>(items, page, pageSize, total);
    }

    private static void ValidateDates(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new BadRequestException("'from' must be on or before 'to'.");
    }
}
