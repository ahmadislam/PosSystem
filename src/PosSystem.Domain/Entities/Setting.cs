using PosSystem.Domain.Common;

namespace PosSystem.Domain.Entities;

public class Setting : BaseEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int? UpdatedByUserId { get; set; }
}
