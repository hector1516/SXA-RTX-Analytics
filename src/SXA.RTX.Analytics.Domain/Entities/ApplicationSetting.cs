using SXA.RTX.Analytics.Domain.Common;

namespace SXA.RTX.Analytics.Domain.Entities;

/// <summary>
/// Key-value application settings editable from UI without recompilation.
/// Future categories: General, Security, Reporting, Appearance, etc.
/// </summary>
public sealed class ApplicationSetting : BaseEntity
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsEncrypted { get; set; }
}
