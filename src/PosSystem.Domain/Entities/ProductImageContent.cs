namespace PosSystem.Domain.Entities;

/// <summary>Optional database storage for hosts without persistent disks.
/// Kept separate from image metadata so catalog queries and audit logs never load image bytes.</summary>
public class ProductImageContent
{
    public int ProductImageId { get; set; }
    public byte[] Bytes { get; set; } = [];
}
