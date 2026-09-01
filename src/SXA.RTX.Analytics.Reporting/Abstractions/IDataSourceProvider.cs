using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Reporting.Abstractions;

/// <summary>
/// Abstraction for executing queries against any data source.
/// Reporting Engine consumes this; UI/API/scheduled jobs never couple to concrete providers.
/// </summary>
public interface IDataSourceProvider
{
    /// <summary>Discriminator for routing / UI display (e.g. "SqlServer", "Odbc").</summary>
    string ProviderName { get; }

    Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default);
}
