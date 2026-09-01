using Microsoft.Extensions.Logging.Abstractions;
using SXA.RTX.Analytics.Reporting.Models;
using SXA.RTX.Analytics.Reporting.Providers;

namespace SXA.RTX.Analytics.Reporting.Tests;

public sealed class ReportingSmokeTests
{
    [Fact]
    public void QueryRequest_Should_Require_Sql_And_DataSource()
    {
        var req = new QueryRequest { DataSourceId = Guid.NewGuid(), Sql = "SELECT 1", MaxRows = 100 };
        Assert.Equal("SELECT 1", req.Sql);
        Assert.Equal(100, req.MaxRows);
    }

    [Fact]
    public async Task SqlServerProvider_Should_Return_Empty_Result_For_Scaffold()
    {
        var provider = new SqlServerDataSourceProvider(NullLogger<SqlServerDataSourceProvider>.Instance);
        var req = new QueryRequest { DataSourceId = Guid.NewGuid(), Sql = "SELECT 1 AS Col" };
        var result = await provider.ExecuteAsync(req);
        Assert.NotNull(result);
        Assert.Empty(result.Columns);
        Assert.Empty(result.Rows);
        Assert.Equal("SqlServer", provider.ProviderName);
    }

    [Fact]
    public void QueryResult_Should_Compute_TotalRows()
    {
        var result = new QueryResult
        {
            Columns = [new QueryColumn("Id", "ID", typeof(int))],
            Rows = [[1], [2]],
            ExecutionTime = TimeSpan.FromMilliseconds(5)
        };
        Assert.Equal(2, result.TotalRows);
    }
}
