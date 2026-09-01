using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Data.Odbc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

public sealed class SqlSystemTableService : ISystemTableService
{
    private readonly ConfigurationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SqlSystemTableService> _logger;

    // Tablas del sistema — prefijo SXA_RTX_ para identidad de la app.
    // Solo estas se permiten crear desde UI (no tablas operacionales).
    private static readonly IReadOnlyList<SystemTableStatus> Definitions = new List<SystemTableStatus>
    {
        new("SXA_RTX_ApplicationSettings", "dbo", false, "Key-value de configuración editable sin recompilar", CreateApplicationSettings),
        new("SXA_RTX_AuditLogs", "dbo", false, "Auditoría de acciones (quién/cuándo/qué)"),
        new("SXA_RTX_Users", "dbo", false, "Usuarios internos de la plataforma"),
        new("SXA_RTX_Roles", "dbo", false, "Roles y permisos"),
        new("SXA_RTX_UserRoles", "dbo", false, "Asignación usuario↔rol (M:N)"),
        new("SXA_RTX_DataSources", "dbo", false, "Catálogo de fuentes (SQL/ODBC) con cadena cifrada"),
    };

    private const string CreateApplicationSettings = @"
IF OBJECT_ID('dbo.SXA_RTX_ApplicationSettings','U') IS NULL
CREATE TABLE dbo.SXA_RTX_ApplicationSettings(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 [Key] nvarchar(200) NOT NULL UNIQUE,
 [Value] nvarchar(4000) NOT NULL,
 [Description] nvarchar(1000) NULL,
 [Category] nvarchar(100) NULL,
 IsEncrypted bit NOT NULL DEFAULT 0,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);";

    private static readonly IReadOnlyDictionary<string,string> CreateSqlByTable = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SXA_RTX_ApplicationSettings"] = CreateApplicationSettings,
        ["SXA_RTX_AuditLogs"] = @"
IF OBJECT_ID('dbo.SXA_RTX_AuditLogs','U') IS NULL
CREATE TABLE dbo.SXA_RTX_AuditLogs(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 TimestampUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 [Action] nvarchar(100) NOT NULL,
 EntityName nvarchar(200) NOT NULL,
 EntityId nvarchar(200) NULL,
 PerformedBy nvarchar(200) NULL,
 Details nvarchar(4000) NULL,
 IpAddress nvarchar(45) NULL,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
); CREATE INDEX IX_SXA_RTX_AuditLogs_TimestampUtc ON dbo.SXA_RTX_AuditLogs(TimestampUtc);",
        ["SXA_RTX_Users"] = @"
IF OBJECT_ID('dbo.SXA_RTX_Users','U') IS NULL
CREATE TABLE dbo.SXA_RTX_Users(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Username nvarchar(100) NOT NULL UNIQUE,
 DisplayName nvarchar(200) NOT NULL,
 Email nvarchar(320) NULL,
 IsActive bit NOT NULL DEFAULT 1,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);",
        ["SXA_RTX_Roles"] = @"
IF OBJECT_ID('dbo.SXA_RTX_Roles','U') IS NULL
CREATE TABLE dbo.SXA_RTX_Roles(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Name nvarchar(100) NOT NULL UNIQUE,
 Description nvarchar(500) NULL,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);",
        ["SXA_RTX_UserRoles"] = @"
IF OBJECT_ID('dbo.SXA_RTX_UserRoles','U') IS NULL
CREATE TABLE dbo.SXA_RTX_UserRoles(
 UserId uniqueidentifier NOT NULL REFERENCES dbo.SXA_RTX_Users(Id),
 RoleId uniqueidentifier NOT NULL REFERENCES dbo.SXA_RTX_Roles(Id),
 AssignedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 PRIMARY KEY(UserId, RoleId)
);",
        ["SXA_RTX_DataSources"] = @"
IF OBJECT_ID('dbo.SXA_RTX_DataSources','U') IS NULL
CREATE TABLE dbo.SXA_RTX_DataSources(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Name nvarchar(150) NOT NULL UNIQUE,
 Type int NOT NULL, -- 1=SqlServer 2=Odbc
 ConnectionStringEncrypted nvarchar(1000) NOT NULL,
 IsActive bit NOT NULL DEFAULT 1,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);",
    };

    public SqlSystemTableService(ConfigurationDbContext db, IConfiguration config, ILogger<SqlSystemTableService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemTableStatus>> GetSystemTablesStatusAsync(CancellationToken ct = default)
    {
        // InMemory (scaffold sin SQL) → simula que las 2 primeras existen (son las del DbContext)
        if (!_db.Database.IsRelational() || _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Definitions.Select((d,i) => d with { Exists = i < 2 }).ToList();
        }

        var result = new List<SystemTableStatus>();
        var conn = _db.Database.GetDbConnection();
        var shouldClose = conn.State != ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);
        try
        {
            foreach (var def in Definitions)
            {
                var exists = await TableExistsAsync(conn, def.Schema, def.TableName, ct);
                result.Add(def with { Exists = exists });
            }
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
        return result;
    }

    public async Task<(bool Success, string Message)> CreateTableAsync(string tableName, CancellationToken ct = default)
    {
        if (!CreateSqlByTable.TryGetValue(tableName, out var sql))
            return (false, $"Tabla del sistema no reconocida: {tableName}");

        if (!_db.Database.IsRelational() || _db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            // En InMemory no hay SQL real — simulamos éxito y logueamos.
            _logger.LogInformation("Simulado CREATE TABLE {Table} en InMemory (sin SQL Server)", tableName);
            return (true, $"[Modo scaffold/InMemory] Se simuló la creación de {tableName}. Con SQL Server real se ejecutará: {sql[..Math.Min(80, sql.Length)]}...");
        }

        try
        {
            var conn = _db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) await conn.OpenAsync(ct);
            try
            {
                // Verificar si ya existe
                var schema = "dbo";
                if (await TableExistsAsync(conn, schema, tableName, ct))
                    return (true, $"{tableName} ya existe.");

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation("Tabla del sistema creada: {Table}", tableName);
                return (true, $"{tableName} creada correctamente.");
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando tabla {Table}", tableName);
            return (false, $"Error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CreateAllMissingAsync(CancellationToken ct = default)
    {
        var statuses = await GetSystemTablesStatusAsync(ct);
        var missing = statuses.Where(s => !s.Exists).ToList();
        if (missing.Count == 0) return (true, "Todas las tablas del sistema ya existen.");
        var msgs = new List<string>();
        foreach (var t in missing)
        {
            var r = await CreateTableAsync(t.TableName, ct);
            msgs.Add($"{t.TableName}: {r.Message}");
            if (!r.Success) return (false, string.Join(" | ", msgs));
        }
        return (true, string.Join(" | ", msgs));
    }

    public async Task<(bool Success, string Message)> TestSqlConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "Cadena vacía.");
        // Evitar loguear password
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return (true, $"Conexión OK — Servidor {conn.DataSource} / DB {conn.Database}");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(connectionString)) return list;
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name", conn);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
        }
        catch { /* silencioso */ }
        return list;
    }

    public async Task<(bool Success, string Message)> TestOdbcConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "Cadena ODBC vacía.");
        try
        {
            using var conn = new OdbcConnection(connectionString);
            await conn.OpenAsync(ct);
            return (true, $"ODBC OK — {conn.Driver} / {conn.DataSource}");
        }
        catch (Exception ex)
        {
            return (false, $"Error ODBC: {ex.Message}");
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection conn, string schema, string table, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@t";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@s"; p1.Value = schema; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@t"; p2.Value = table; cmd.Parameters.Add(p2);
        var o = await cmd.ExecuteScalarAsync(ct);
        return o != null;
    }
}
