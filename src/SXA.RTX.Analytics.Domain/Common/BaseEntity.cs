namespace SXA.RTX.Analytics.Domain.Common;

/// <summary>
/// Base entity with audit fields. All configuration entities inherit from this.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
