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
    // Esta lista ES la fuente de verdad del esquema de configuración: debe coincidir
    // con el modelo de EF Core (Configurations). Si en una actualización se agrega una
    // entidad nueva, se añade aquí su definición y el botón "Crear faltantes" la crea.
    private static readonly IReadOnlyList<SystemTableStatus> Definitions = new List<SystemTableStatus>
    {
        new("SXA_RTX_ApplicationSettings", "dbo", false, "Key-value de configuración editable sin recompilar", CreateApplicationSettings),
        new("SXA_RTX_AuditLogs", "dbo", false, "Bitácora de acciones (quién/cuándo/qué)", CreateAuditLogs),
        new("SXA_RTX_Equipos", "dbo", false, "Mapeo DeviceId → Nombre/Área/Tipo", CreateEquipos),
        new("SXA_RTX_TablasConfig", "dbo", false, "Tablas operativas VTI/VTech + título amigable", CreateTablasConfig),
        new("SXA_RTX_TablaColumnas", "dbo", false, "Columnas visibles por tabla", CreateTablaColumnas),
        new("SXA_RTX_Users", "dbo", false, "Usuarios internos de la plataforma", CreateUsers),
    };

    private const string CreateApplicationSettings = @"
IF OBJECT_ID('dbo.SXA_RTX_ApplicationSettings','U') IS NULL
CREATE TABLE dbo.SXA_RTX_ApplicationSettings(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 [Key] nvarchar(200) NOT NULL,
 [Value] nvarchar(4000) NOT NULL,
 [Description] nvarchar(1000) NULL,
 Category nvarchar(100) NULL,
 IsEncrypted bit NOT NULL DEFAULT 0,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SXA_RTX_ApplicationSettings_Key' AND object_id=OBJECT_ID('dbo.SXA_RTX_ApplicationSettings'))
CREATE UNIQUE INDEX IX_SXA_RTX_ApplicationSettings_Key ON dbo.SXA_RTX_ApplicationSettings([Key]);";

    private const string CreateAuditLogs = @"
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
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SXA_RTX_AuditLogs_TimestampUtc' AND object_id=OBJECT_ID('dbo.SXA_RTX_AuditLogs'))
CREATE INDEX IX_SXA_RTX_AuditLogs_TimestampUtc ON dbo.SXA_RTX_AuditLogs(TimestampUtc);";

    private const string CreateEquipos = @"
IF OBJECT_ID('dbo.SXA_RTX_Equipos','U') IS NULL
CREATE TABLE dbo.SXA_RTX_Equipos(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 DeviceId nvarchar(64) NOT NULL,
 Nombre nvarchar(150) NOT NULL,
 Area nvarchar(100) NOT NULL,
 Tipo int NOT NULL DEFAULT 0,
 Descripcion nvarchar(500) NULL,
 IsActive bit NOT NULL DEFAULT 1,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SXA_RTX_Equipos_DeviceId' AND object_id=OBJECT_ID('dbo.SXA_RTX_Equipos'))
CREATE UNIQUE INDEX IX_SXA_RTX_Equipos_DeviceId ON dbo.SXA_RTX_Equipos(DeviceId);";

    private const string CreateTablasConfig = @"
IF OBJECT_ID('dbo.SXA_RTX_TablasConfig','U') IS NULL
CREATE TABLE dbo.SXA_RTX_TablasConfig(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 TableName nvarchar(260) NOT NULL,
 Fuente nvarchar(100) NOT NULL,
 DisplayName nvarchar(200) NULL,
 SerialColumn nvarchar(200) NULL,
 DateColumn nvarchar(200) NULL,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);
IF COL_LENGTH('dbo.SXA_RTX_TablasConfig','Fuente') IS NULL ALTER TABLE dbo.SXA_RTX_TablasConfig ADD Fuente nvarchar(100) NULL;
IF COL_LENGTH('dbo.SXA_RTX_TablasConfig','DisplayName') IS NULL ALTER TABLE dbo.SXA_RTX_TablasConfig ADD DisplayName nvarchar(200) NULL;
IF COL_LENGTH('dbo.SXA_RTX_TablasConfig','SerialColumn') IS NULL ALTER TABLE dbo.SXA_RTX_TablasConfig ADD SerialColumn nvarchar(200) NULL;
IF COL_LENGTH('dbo.SXA_RTX_TablasConfig','DateColumn') IS NULL ALTER TABLE dbo.SXA_RTX_TablasConfig ADD DateColumn nvarchar(200) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SXA_RTX_TablasConfig_TableName' AND object_id=OBJECT_ID('dbo.SXA_RTX_TablasConfig'))
CREATE UNIQUE INDEX IX_SXA_RTX_TablasConfig_TableName ON dbo.SXA_RTX_TablasConfig(TableName);";

    private const string CreateTablaColumnas = @"
IF OBJECT_ID('dbo.SXA_RTX_TablaColumnas','U') IS NULL
BEGIN
CREATE TABLE dbo.SXA_RTX_TablaColumnas(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 TablaConfigId uniqueidentifier NOT NULL,
 ColumnName nvarchar(200) NOT NULL,
 DisplayName nvarchar(200) NULL,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);
CREATE UNIQUE INDEX IX_SXA_RTX_TablaColumnas_TablaCol ON dbo.SXA_RTX_TablaColumnas(TablaConfigId, ColumnName);
ALTER TABLE dbo.SXA_RTX_TablaColumnas ADD CONSTRAINT FK_SXA_RTX_TablaColumnas_TablasConfig FOREIGN KEY (TablaConfigId) REFERENCES dbo.SXA_RTX_TablasConfig(Id) ON DELETE CASCADE;
END
IF COL_LENGTH('dbo.SXA_RTX_TablaColumnas','DisplayName') IS NULL ALTER TABLE dbo.SXA_RTX_TablaColumnas ADD DisplayName nvarchar(200) NULL;";

    private const string CreateUsers = @"
IF OBJECT_ID('dbo.SXA_RTX_Users','U') IS NULL
CREATE TABLE dbo.SXA_RTX_Users(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Username nvarchar(100) NOT NULL,
 PasswordHash nvarchar(500) NOT NULL,
 DisplayName nvarchar(200) NOT NULL,
 Role int NOT NULL DEFAULT 2,
 IsActive bit NOT NULL DEFAULT 1,
 MustChangePassword bit NOT NULL DEFAULT 0,
 CreatedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAtUtc datetime2 NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SXA_RTX_Users_Username' AND object_id=OBJECT_ID('dbo.SXA_RTX_Users'))
CREATE UNIQUE INDEX IX_SXA_RTX_Users_Username ON dbo.SXA_RTX_Users(Username);";

    public SqlSystemTableService(ConfigurationDbContext db, IConfiguration config, ILogger<SqlSystemTableService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public Task<IReadOnlyList<SystemTableStatus>> GetSystemTablesStatusAsync(CancellationToken ct = default)
        // La base SQL es de solo lectura: la configuracion vive en SQLite. Nada que listar/crear aqui.
        => Task.FromResult<IReadOnlyList<SystemTableStatus>>(Array.Empty<SystemTableStatus>());

    public Task<(bool Success, string Message)> CreateTableAsync(string tableName, CancellationToken ct = default)
        => Task.FromResult((false, "La base SQL es de solo lectura: la aplicacion no crea ni modifica tablas."));

    public Task<(bool Success, string Message)> CreateAllMissingAsync(CancellationToken ct = default)
        => Task.FromResult((false, "La base SQL es de solo lectura: la aplicacion no crea ni modifica tablas."));

    public async Task<(bool Success, string Message)> TestSqlConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "Cadena vacía.");
        // Evitar loguear password
        try
        {
            using var conn = SqlProbe.Create(connectionString);
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
            using var conn = SqlProbe.Create(connectionString);
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
