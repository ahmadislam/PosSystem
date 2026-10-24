using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class Customer : BaseEntity, ISoftDelete
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public int LoyaltyPoints { get; set; }
    public uint RowVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
}
