using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class User : BaseEntity, ISoftDelete
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Cashier;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
}
