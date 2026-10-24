using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;
using PosSystem.Application.Dtos;
using PosSystem.Domain.Entities;

namespace PosSystem.Application.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> ListAsync(string? search, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(int id, CustomerRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LoyaltyTransactionDto>> LoyaltyHistoryAsync(int id, CancellationToken ct = default);
}

public sealed class CustomerService(IAppDbContext db) : ICustomerService
{
    public async Task<IReadOnlyList<CustomerDto>> ListAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(value) ||
                (c.Email != null && c.Email.ToLower().Contains(value)) ||
                (c.Phone != null && c.Phone.Contains(value)));
        }
        return (await query.OrderBy(c => c.Name).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct = default)
    {
        var customer = new Customer
        {
            Name = request.Name.Trim(), Email = NormalizeEmail(request.Email),
            Phone = Clean(request.Phone), Address = Clean(request.Address), IsActive = request.IsActive
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(int id, CustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.Name = request.Name.Trim();
        customer.Email = NormalizeEmail(request.Email);
        customer.Phone = Clean(request.Phone);
        customer.Address = Clean(request.Address);
        customer.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LoyaltyTransactionDto>> LoyaltyHistoryAsync(int id, CancellationToken ct = default)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct))
            throw new NotFoundException("Customer not found.");
        return await db.LoyaltyTransactions.AsNoTracking().Where(t => t.CustomerId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new LoyaltyTransactionDto(t.Id, t.CreatedAt, t.PointsChange, t.BalanceAfter, t.Type, t.Note))
            .Take(100).ToListAsync(ct);
    }

    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Name, c.Email, c.Phone, c.Address, c.LoyaltyPoints, c.IsActive);
    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
