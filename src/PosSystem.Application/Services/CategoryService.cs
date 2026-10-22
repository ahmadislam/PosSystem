using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct = default);
    Task<CategoryDto> UpdateAsync(int id, CategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class CategoryService(IAppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description)).ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name, ct))
            throw new ConflictException($"Category '{name}' already exists.");

        var category = new Category { Name = name, Description = request.Description };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Description);
    }

    public async Task<CategoryDto> UpdateAsync(int id, CategoryRequest request, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != id, ct))
            throw new ConflictException($"Category '{name}' already exists.");

        category.Name = name;
        category.Description = request.Description;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Description);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category not found.");
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
            throw new BadRequestException("Cannot delete a category that still has products.");

        db.Categories.Remove(category);   // converted to a soft delete in AppDbContext
        await db.SaveChangesAsync(ct);
    }
}
