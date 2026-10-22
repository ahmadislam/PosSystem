namespace PosSystem.Domain.Common;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Entities implementing this are never physically deleted (see AppDbContext).</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}
