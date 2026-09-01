namespace SXA.RTX.Analytics.Application.Abstractions;

public sealed record SystemTableStatus(
    string TableName,
    string Schema,
    bool Exists,
    string Purpose,
    string? CreateSql = null);

public sealed record DatabaseOption(string Name, bool IsSelected);

public interface ISystemTableService
{
    Task<IReadOnlyList<SystemTableStatus>> GetSystemTablesStatusAsync(CancellationToken ct = default);
    Task<(bool Success, string Message)> CreateTableAsync(string tableName, CancellationToken ct = default);
    Task<(bool Success, string Message)> CreateAllMissingAsync(CancellationToken ct = default);

    Task<(bool Success, string Message)> TestSqlConnectionAsync(string connectionString, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default);
    Task<(bool Success, string Message)> TestOdbcConnectionAsync(string connectionString, CancellationToken ct = default);
}
