/* =============================================================================
   SXA-RTX Analytics — inicializacion de tablas de configuracion
   -----------------------------------------------------------------------------
   Base:   hussmann_Insight  (la MISMA base operacional, segun decision del cliente)
   Server: 10.96.16.114

   Como la base YA EXISTE, el EnsureCreated() de la aplicacion NO crea estas tablas.
   Ejecuta este script UNA VEZ con una cuenta que tenga permisos de DDL
   (p. ej. sa). Es idempotente: puedes volver a ejecutarlo sin romper nada.

   La app SOLO crea/lee/actualiza las tablas SXA_RTX_*. NO toca las tablas de planta
   (VTi_*, VTech_*, Registros, SXA_PCs, etc.).
   ============================================================================= */

USE [hussmann_Insight];
GO

/* ---- SXA_RTX_ApplicationSettings ---- */
IF OBJECT_ID('dbo.SXA_RTX_ApplicationSettings','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_ApplicationSettings
    (
        Id            uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_ApplicationSettings PRIMARY KEY,
        [Key]         nvarchar(200)    NOT NULL,
        [Value]       nvarchar(4000)   NOT NULL,
        [Description] nvarchar(1000)   NULL,
        Category      nvarchar(100)    NULL,
        IsEncrypted   bit              NOT NULL CONSTRAINT DF_SXA_RTX_ApplicationSettings_IsEncrypted DEFAULT (0),
        CreatedAtUtc  datetime2        NOT NULL,
        UpdatedAtUtc  datetime2        NULL
    );
    CREATE UNIQUE INDEX IX_SXA_RTX_ApplicationSettings_Key ON dbo.SXA_RTX_ApplicationSettings([Key]);
END
GO

/* ---- SXA_RTX_AuditLogs ---- */
IF OBJECT_ID('dbo.SXA_RTX_AuditLogs','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_AuditLogs
    (
        Id           uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_AuditLogs PRIMARY KEY,
        TimestampUtc datetime2        NOT NULL,
        Action       nvarchar(100)    NOT NULL,
        EntityName   nvarchar(200)    NOT NULL,
        EntityId     nvarchar(200)    NULL,
        PerformedBy  nvarchar(200)    NULL,
        Details      nvarchar(4000)   NULL,
        IpAddress    nvarchar(45)     NULL,
        CreatedAtUtc datetime2        NOT NULL,
        UpdatedAtUtc datetime2        NULL
    );
    CREATE INDEX IX_SXA_RTX_AuditLogs_TimestampUtc ON dbo.SXA_RTX_AuditLogs(TimestampUtc);
END
GO

/* ---- SXA_RTX_Equipos ---- */
IF OBJECT_ID('dbo.SXA_RTX_Equipos','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_Equipos
    (
        Id          uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_Equipos PRIMARY KEY,
        DeviceId    nvarchar(64)     NOT NULL,
        Nombre      nvarchar(150)    NOT NULL,
        Area        nvarchar(100)    NOT NULL,
        Tipo        int              NOT NULL CONSTRAINT DF_SXA_RTX_Equipos_Tipo DEFAULT (0), -- 1=VTI 2=VTech 0=desconocido
        Descripcion nvarchar(500)    NULL,
        IsActive    bit              NOT NULL CONSTRAINT DF_SXA_RTX_Equipos_IsActive DEFAULT (1),
        CreatedAtUtc datetime2       NOT NULL,
        UpdatedAtUtc datetime2       NULL
    );
    CREATE UNIQUE INDEX IX_SXA_RTX_Equipos_DeviceId ON dbo.SXA_RTX_Equipos(DeviceId);
END
GO

/* ---- SXA_RTX_TablasConfig ---- */
IF OBJECT_ID('dbo.SXA_RTX_TablasConfig','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_TablasConfig
    (
        Id          uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_TablasConfig PRIMARY KEY,
        TableName   nvarchar(260)    NOT NULL,
        Tipo        int              NOT NULL, -- 1=VTI 2=VTech
        DisplayName nvarchar(200)    NULL,      -- titulo amigable (null = mostrar TableName)
        CreatedAtUtc datetime2       NOT NULL,
        UpdatedAtUtc datetime2       NULL
    );
    CREATE UNIQUE INDEX IX_SXA_RTX_TablasConfig_TableName ON dbo.SXA_RTX_TablasConfig(TableName);
END
GO

/* ---- SXA_RTX_TablaColumnas ---- */
IF OBJECT_ID('dbo.SXA_RTX_TablaColumnas','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_TablaColumnas
    (
        Id            uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_TablaColumnas PRIMARY KEY,
        TablaConfigId uniqueidentifier NOT NULL,
        ColumnName    nvarchar(200)    NOT NULL,
        CreatedAtUtc  datetime2        NOT NULL,
        UpdatedAtUtc  datetime2        NULL
    );
    CREATE UNIQUE INDEX IX_SXA_RTX_TablaColumnas_TablaCol
        ON dbo.SXA_RTX_TablaColumnas(TablaConfigId, ColumnName);
    ALTER TABLE dbo.SXA_RTX_TablaColumnas
        ADD CONSTRAINT FK_SXA_RTX_TablaColumnas_TablasConfig
        FOREIGN KEY (TablaConfigId) REFERENCES dbo.SXA_RTX_TablasConfig(Id) ON DELETE CASCADE;
END
GO

/* ---- SXA_RTX_Users ---- */
IF OBJECT_ID('dbo.SXA_RTX_Users','U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_RTX_Users
    (
        Id                 uniqueidentifier NOT NULL CONSTRAINT PK_SXA_RTX_Users PRIMARY KEY,
        Username           nvarchar(100)    NOT NULL,
        PasswordHash       nvarchar(500)    NOT NULL,
        DisplayName        nvarchar(200)    NOT NULL,
        Role               int              NOT NULL, -- 1=Administrador 2=Usuario
        IsActive           bit              NOT NULL CONSTRAINT DF_SXA_RTX_Users_IsActive DEFAULT (1),
        MustChangePassword bit              NOT NULL CONSTRAINT DF_SXA_RTX_Users_MustChangePassword DEFAULT (0),
        CreatedAtUtc       datetime2        NOT NULL,
        UpdatedAtUtc       datetime2        NULL
    );
    CREATE UNIQUE INDEX IX_SXA_RTX_Users_Username ON dbo.SXA_RTX_Users(Username);
END
GO

/* =============================================================================
   Permisos para la cuenta de aplicacion.
   La cuenta necesita:
     - SELECT sobre las tablas de planta (VTi_*, VTech_*, Registros, SXA_PCs...)
     - SELECT/INSERT/UPDATE/DELETE sobre las tablas SXA_RTX_*.
   Si HInsightUser ya tiene db_datareader + db_datawriter, este bloque es redundante.
   ============================================================================= */
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'HInsightUser')
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_ApplicationSettings TO HInsightUser;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_AuditLogs          TO HInsightUser;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_Equipos            TO HInsightUser;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_TablasConfig       TO HInsightUser;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_TablaColumnas      TO HInsightUser;
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SXA_RTX_Users              TO HInsightUser;
END
GO

PRINT 'Tablas de configuracion de SXA-RTX Analytics listas en hussmann_Insight.';
GO
