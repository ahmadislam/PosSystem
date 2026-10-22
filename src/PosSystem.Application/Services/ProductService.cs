using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;
using PosSystem.Domain.Enums;

namespace PosSystem.Application.Services;

public interface IProductService
{
    Task<PagedResult<ProductDto>> SearchAsync(string? search, int? categoryId, bool lowStockOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ProductDto> GetAsync(int id, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(int id, ProductRequest request, CancellationToken ct = default);
    Task<ProductDto> AdjustStockAsync(int id, StockAdjustRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class ProductService(IAppDbContext db, ICurrentUser current) : IProductService
{
    public async Task<PagedResult<ProductDto>> SearchAsync(string? search, int? categoryId, bool lowStockOnly,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || p.Sku.ToLower().Contains(s));
        }
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
        if (lowStockOnly) query = query.Where(p => p.StockQuantity <= p.MinStockThreshold);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ProductDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product not found.");
        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        var sku = request.Sku.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Sku == sku, ct)) throw new ConflictException($"SKU '{sku}' already exists.");
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) throw new BadRequestException("Category does not exist.");

        var product = new Product
        {
            Sku = sku, Name = request.Name.Trim(), Description = request.Description, CategoryId = request.CategoryId,
            CostPrice = request.CostPrice, SalePrice = request.SalePrice, TaxRate = request.TaxRate,
            StockQuantity = request.StockQuantity, MinStockThreshold = request.MinStockThreshold, IsActive = request.IsActive
        };
        db.Products.Add(product);
        if (request.StockQuantity > 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                Product = product, CreatedByUserId = userId, Type = StockMovementType.Adjustment,
                QuantityChange = request.StockQuantity, Note = "Initial stock"
            });
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(int id, ProductRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        var sku = request.Sku.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != id, ct)) throw new ConflictException($"SKU '{sku}' already exists.");
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) throw new BadRequestException("Category does not exist.");

        product.Sku = sku; product.Name = request.Name.Trim(); product.Description = request.Description;
        product.CategoryId = request.CategoryId; product.CostPrice = request.CostPrice; product.SalePrice = request.SalePrice;
        product.TaxRate = request.TaxRate; product.MinStockThreshold = request.MinStockThreshold; product.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> AdjustStockAsync(int id, StockAdjustRequest request, CancellationToken ct = default)
    {
        var userId = current.UserId ?? throw new UnauthorizedAppException("Not signed in.");
        if (request.QuantityChange == 0) throw new BadRequestException("Quantity change cannot be zero.");

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        if (product.StockQuantity + request.QuantityChange < 0) throw new BadRequestException("Stock cannot go below zero.");

        product.StockQuantity += request.QuantityChange;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id, CreatedByUserId = userId, Type = StockMovementType.Adjustment,
            QuantityChange = request.QuantityChange, Note = request.Note
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product not found.");
        db.Products.Remove(product);   // converted to a soft delete in AppDbContext
        await db.SaveChangesAsync(ct);
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Sku, p.Name, p.Description, p.CategoryId, p.Category.Name,
        p.CostPrice, p.SalePrice, p.TaxRate, p.StockQuantity, p.MinStockThreshold, p.IsActive,
        p.StockQuantity <= p.MinStockThreshold);
}
