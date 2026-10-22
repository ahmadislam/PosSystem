using PosSystem.Domain.Common;
using PosSystem.Domain.Enums;

namespace PosSystem.Domain.Entities;

public class AuditLog : BaseEntity
{
    public int? UserId { get; set; }          // null for system jobs
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AuditAction Action { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }    // JSON
    public string? NewValues { get; set; }    // JSON
}
