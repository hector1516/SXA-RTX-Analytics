namespace SXA.RTX.Analytics.Reporting.Models;

/// <summary>
/// Represents "what data to fetch". Separated from Report (= how to present) by design.
/// </summary>
public sealed record QueryRequest
{
    public required Guid DataSourceId { get; init; }
    public required string Sql { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public int? MaxRows { get; init; }
    public TimeSpan? Timeout { get; init; }
}
