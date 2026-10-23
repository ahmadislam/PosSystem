using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> ListAsync(CancellationToken ct = default);
    Task<SupplierDto> GetAsync(int id, CancellationToken ct = default);
    Task<SupplierDto> CreateAsync(SupplierRequest request, CancellationToken ct = default);
    Task<SupplierDto> UpdateAsync(int id, SupplierRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public class SupplierService(IAppDbContext db) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierDto>> ListAsync(CancellationToken ct = default) =>
        await db.Suppliers.AsNoTracking().OrderBy(s => s.Name).Select(s => ToDto(s)).ToListAsync(ct);

    public async Task<SupplierDto> GetAsync(int id, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Supplier not found.");
        return ToDto(supplier);
    }

    public async Task<SupplierDto> CreateAsync(SupplierRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        await EnsureNameAvailable(name, null, ct);
        var supplier = new Supplier
        {
            Name = name,
            ContactName = TrimOrNull(request.ContactName),
            Email = TrimOrNull(request.Email),
            Phone = TrimOrNull(request.Phone),
            Address = TrimOrNull(request.Address),
            Notes = TrimOrNull(request.Notes),
            IsActive = request.IsActive
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return ToDto(supplier);
    }

    public async Task<SupplierDto> UpdateAsync(int id, SupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Supplier not found.");
        var name = request.Name.Trim();
        await EnsureNameAvailable(name, id, ct);
        supplier.Name = name;
        supplier.ContactName = TrimOrNull(request.ContactName);
        supplier.Email = TrimOrNull(request.Email);
        supplier.Phone = TrimOrNull(request.Phone);
        supplier.Address = TrimOrNull(request.Address);
        supplier.Notes = TrimOrNull(request.Notes);
        supplier.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(supplier);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Supplier not found.");
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameAvailable(string name, int? exceptId, CancellationToken ct)
    {
        var normalized = name.ToLower();
        if (await db.Suppliers.AnyAsync(s => s.Name.ToLower() == normalized && s.Id != exceptId, ct))
            throw new ConflictException($"Supplier '{name}' already exists.");
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SupplierDto ToDto(Supplier s) =>
        new(s.Id, s.Name, s.ContactName, s.Email, s.Phone, s.Address, s.Notes, s.IsActive);
}
