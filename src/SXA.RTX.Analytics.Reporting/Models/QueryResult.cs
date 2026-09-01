namespace SXA.RTX.Analytics.Reporting.Models;

public sealed record QueryColumn(
    string Name,
    string DisplayName,
    Type DataType,
    bool IsVisible = true,
    string? Format = null);

public sealed record QueryResult
{
    public required IReadOnlyList<QueryColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public int TotalRows => Rows.Count;
    public TimeSpan ExecutionTime { get; init; }
    public bool IsTruncated { get; init; }
}
