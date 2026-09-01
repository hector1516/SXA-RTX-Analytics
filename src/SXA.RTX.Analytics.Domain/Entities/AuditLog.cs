using SXA.RTX.Analytics.Domain.Common;

namespace SXA.RTX.Analytics.Domain.Entities;

/// <summary>
/// Minimal audit trail for configuration actions. Extended in later phases.
/// </summary>
public sealed class AuditLog : BaseEntity
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? PerformedBy { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
