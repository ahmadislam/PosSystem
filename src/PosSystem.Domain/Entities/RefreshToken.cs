using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;   // SHA-256 of the raw token, never the raw token
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? ReplacedByTokenId { get; set; }
}
