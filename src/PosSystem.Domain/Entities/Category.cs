using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class Category : BaseEntity, ISoftDelete
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDeleted { get; set; }
}
