using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Reporting.Abstractions;
using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Reporting.Providers;

/// <summary>
/// SQL Server implementation of <see cref="IDataSourceProvider"/>.
/// Production usage must use read-only credentials; connection string resolution is delegated to caller.
/// ODBC provider will be added in a later phase (MAPICS).
/// </summary>
public sealed class SqlServerDataSourceProvider : IDataSourceProvider
{
    private readonly ILogger<SqlServerDataSourceProvider> _logger;

    public SqlServerDataSourceProvider(ILogger<SqlServerDataSourceProvider> logger)
    {
        _logger = logger;
    }

    public string ProviderName => "SqlServer";

    public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        // Placeholder: full implementation in Phase 2. This scaffold validates wiring + logging + timing.
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Sql);

        _logger.LogInformation("Executing SqlServer query for DataSource {DataSourceId} (MaxRows={MaxRows})",
            request.DataSourceId, request.MaxRows);

        var sw = Stopwatch.StartNew();

        // In scaffold mode we do not open a real connection — return empty result with metadata.
        // Real implementation: open SqlConnection, create SqlCommand, add parameters, ExecuteReaderAsync, map to QueryResult.
        await Task.Delay(10, cancellationToken);

        sw.Stop();

        return new QueryResult
        {
            Columns = [],
            Rows = [],
            ExecutionTime = sw.Elapsed,
            IsTruncated = false
        };
    }
}
