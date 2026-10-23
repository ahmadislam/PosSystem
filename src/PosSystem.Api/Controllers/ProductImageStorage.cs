using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Api.Controllers;

public sealed class ProductImageStorage(
    IAppDbContext db,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    private const long MaxBytes = 4 * 1024 * 1024;
    private readonly string _directory = Path.GetFullPath(configuration["ProductImages:Directory"]
        ?? Path.Combine(environment.ContentRootPath, "App_Data", "product-images"));

    public async Task<ProductImage> SaveAsync(int productId, IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxBytes)
            throw new BadRequestException("Image must be between 1 byte and 4 MB.");
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
            throw new NotFoundException("Product not found.");

        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        var (contentType, extension) = Identify(bytes);
        var key = $"{Guid.NewGuid():N}{extension}";
        Directory.CreateDirectory(_directory);
        var path = Resolve(key);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            var existing = await db.ProductImages.Where(image => image.ProductId == productId).ToListAsync(ct);
            foreach (var image in existing) image.IsPrimary = false;
            if (existing.Count > 0) await db.SaveChangesAsync(ct);
            var saved = new ProductImage { ProductId = productId, StorageKey = key, ContentType = contentType, IsPrimary = true };
            db.ProductImages.Add(saved);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return saved;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProductImageDto>> ListAsync(int productId, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(product => product.Id == productId, ct))
            throw new NotFoundException("Product not found.");
        return await db.ProductImages.AsNoTracking().Where(image => image.ProductId == productId)
            .OrderByDescending(image => image.CreatedAt)
            .Select(image => new ProductImageDto(image.Id, image.ContentType, image.IsPrimary,
                $"/api/products/{productId}/images/{image.Id}"))
            .ToListAsync(ct);
    }

    public async Task SetPrimaryAsync(int productId, int imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.FirstOrDefaultAsync(image =>
            image.ProductId == productId && image.Id == imageId, ct)
            ?? throw new NotFoundException("Product image not found.");
        if (image.IsPrimary) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ProductImages.FirstOrDefaultAsync(
            other => other.ProductId == productId && other.IsPrimary, ct);
        if (current is not null)
        {
            current.IsPrimary = false;
            await db.SaveChangesAsync(ct);
        }
        image.IsPrimary = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<(ProductImage Image, string Path)> GetAsync(int productId, int imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.AsNoTracking()
            .FirstOrDefaultAsync(image => image.ProductId == productId && image.Id == imageId, ct)
            ?? throw new PosSystem.Application.Common.NotFoundException("Product image not found.");
        var path = Resolve(image.StorageKey);
        if (!File.Exists(path)) throw new InvalidOperationException("The stored product image file is missing.");
        return (image, path);
    }

    public async Task DeleteAsync(int productId, int imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.FirstOrDefaultAsync(image =>
            image.ProductId == productId && image.Id == imageId, ct)
            ?? throw new PosSystem.Application.Common.NotFoundException("Product image not found.");
        var path = Resolve(image.StorageKey);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wasPrimary = image.IsPrimary;
        if (wasPrimary)
        {
            image.IsPrimary = false;
            await db.SaveChangesAsync(ct);
        }
        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(ct);
        if (wasPrimary)
        {
            var next = await db.ProductImages.Where(other => other.ProductId == productId)
                .OrderByDescending(other => other.CreatedAt).FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsPrimary = true;
                await db.SaveChangesAsync(ct);
            }
        }
        await transaction.CommitAsync(ct);
        if (File.Exists(path)) File.Delete(path);
    }

    private string Resolve(string key)
    {
        if (Path.GetFileName(key) != key) throw new InvalidOperationException("Invalid product image storage key.");
        return Path.Combine(_directory, key);
    }

    private static (string ContentType, string Extension) Identify(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ".png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", ".jpg");
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return ("image/webp", ".webp");
        throw new PosSystem.Application.Common.BadRequestException("Only PNG, JPEG, and WebP images are supported.");
    }
}
