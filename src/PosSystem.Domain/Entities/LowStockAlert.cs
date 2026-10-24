using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class LowStockAlert : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime DetectedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int DeliveryAttempts { get; set; }
    public string? LastError { get; set; }
}
