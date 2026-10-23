using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class Product : BaseEntity, ISoftDelete
{
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? DefaultSupplierId { get; set; }
    public Supplier? DefaultSupplier { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    /// <summary>Percentage, e.g. 17 means 17%.</summary>
    public decimal TaxRate { get; set; }
    public int StockQuantity { get; set; }
    public int MinStockThreshold { get; set; }
    /// <summary>Concurrency token (PostgreSQL xmin).</summary>
    public uint RowVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
}
