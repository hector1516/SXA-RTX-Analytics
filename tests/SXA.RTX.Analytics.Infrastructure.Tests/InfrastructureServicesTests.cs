using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;
using SXA.RTX.Analytics.Infrastructure.Services;

namespace SXA.RTX.Analytics.Infrastructure.Tests;

/// <summary>Base con un ConfigurationDbContext InMemory aislado por test.</summary>
public abstract class InfrastructureTestBase
{
    protected static ConfigurationDbContext NewDb(string name) => new(new DbContextOptionsBuilder<ConfigurationDbContext>()
        .UseInMemoryDatabase(name)
        .Options);

    protected static ApplicationSettingService NewSettingsService(ConfigurationDbContext db) =>
        new(db, NullLogger<ApplicationSettingService>.Instance, new SecretProtector(NullLogger<SecretProtector>.Instance));

    protected static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value))).Build();
}

/// <summary>
/// La precedencia de las conexiones es critica: si /query apuntara a la base equivocada
/// filtraria datos de otra planta, asi que se fija con tests.
/// </summary>
public class DataSourceServiceTests : InfrastructureTestBase
{
    [Fact]
    public async Task Sin_ninguna_fuente_devuelve_null_en_las_tres()
    {
        await using var db = NewDb(nameof(Sin_ninguna_fuente_devuelve_null_en_las_tres));
        var sut = new DataSourceService(NewSettingsService(db), Config(), NullLogger<DataSourceService>.Instance);

        var s = await sut.GetAsync();

        Assert.Null(s.ConfigurationConnectionString);
        Assert.Null(s.OperationalConnectionString);
        Assert.Null(s.OdbcConnectionString);
        Assert.True(s.OperationalFallsBackToConfiguration);
    }

    [Fact]
    public async Task Usa_appsettings_cuando_no_hay_valor_guardado()
    {
        await using var db = NewDb(nameof(Usa_appsettings_cuando_no_hay_valor_guardado));
        var sut = new DataSourceService(NewSettingsService(db), Config(
            ("ConnectionStrings:ConfigurationDatabase", "Server=cfg;Database=Analytics;"),
            ("ConnectionStrings:OperationalDatabase", "Server=op;Database=Planta;")), NullLogger<DataSourceService>.Instance);

        var s = await sut.GetAsync();

        Assert.Equal("Server=cfg;Database=Analytics;", s.ConfigurationConnectionString);
        Assert.Equal("Server=op;Database=Planta;", s.OperationalConnectionString);
        Assert.False(s.OperationalFallsBackToConfiguration);
        Assert.Equal("Server=op;Database=Planta;", s.EffectiveOperationalConnectionString);
    }

    [Fact]
    public async Task El_valor_guardado_gana_a_appsettings()
    {
        await using var db = NewDb(nameof(El_valor_guardado_gana_a_appsettings));
        var settings = NewSettingsService(db);
        var sut = new DataSourceService(settings, Config(
            ("ConnectionStrings:ConfigurationDatabase", "Server=appsettings;Database=Analytics;"),
            ("ConnectionStrings:OperationalDatabase", "Server=appsettings;Database=Planta;")), NullLogger<DataSourceService>.Instance);

        await sut.SaveAsync("Server=guardado;Database=Analytics;", "Server=guardado;Database=Planta;", null);

        var s = await sut.GetAsync();

        Assert.Equal("Server=guardado;Database=Analytics;", s.ConfigurationConnectionString);
        Assert.Equal("Server=guardado;Database=Planta;", s.OperationalConnectionString);
    }

    [Fact]
    public async Task Guardar_una_cadena_vacia_revierte_a_appsettings()
    {
        await using var db = NewDb(nameof(Guardar_una_cadena_vacia_revierte_a_appsettings));
        var settings = NewSettingsService(db);
        var sut = new DataSourceService(settings, Config(
            ("ConnectionStrings:ConfigurationDatabase", "Server=appsettings;Database=Analytics;")), NullLogger<DataSourceService>.Instance);

        await sut.SaveAsync("Server=guardado;Database=Analytics;", null, null);
        Assert.Equal("Server=guardado;Database=Analytics;", (await sut.GetAsync()).ConfigurationConnectionString);

        await sut.SaveAsync("", null, null);   // borrar el override

        Assert.Equal("Server=appsettings;Database=Analytics;", (await sut.GetAsync()).ConfigurationConnectionString);
    }

    [Fact]
    public void EffectiveOperational_cae_a_configuracion_cuando_no_hay_operacional()
    {
        var s = new DataSourceSettings("Server=cfg;", null, null);

        Assert.True(s.OperationalFallsBackToConfiguration);
        Assert.Equal("Server=cfg;", s.EffectiveOperationalConnectionString);
    }

    [Fact]
    public void EffectiveOperational_prefiere_la_base_operacional()
    {
        var s = new DataSourceSettings("Server=cfg;", "Server=op;", null);

        Assert.False(s.OperationalFallsBackToConfiguration);
        Assert.Equal("Server=op;", s.EffectiveOperationalConnectionString);
    }
}

public class ApplicationSettingServiceTests : InfrastructureTestBase
{
    [Fact]
    public async Task Guarda_y_recupera_un_valor_no_secreto()
    {
        await using var db = NewDb(nameof(Guarda_y_recupera_un_valor_no_secreto));
        var sut = NewSettingsService(db);

        await sut.SetValueAsync("App.UpdateCheckMinutes", "30");

        Assert.Equal("30", await sut.GetValueAsync("App.UpdateCheckMinutes"));
    }

    [Fact]
    public async Task Actualiza_en_vez_de_duplicar_la_clave()
    {
        await using var db = NewDb(nameof(Actualiza_en_vez_de_duplicar_la_clave));
        var sut = NewSettingsService(db);

        await sut.SetValueAsync("App.UpdateCheckMinutes", "30");
        await sut.SetValueAsync("App.UpdateCheckMinutes", "90");

        var rows = await db.Set<ApplicationSetting>().ToListAsync();
        Assert.Single(rows);
        Assert.Equal("90", await sut.GetValueAsync("App.UpdateCheckMinutes"));
    }

    [Fact]
    public async Task Una_clave_inexistente_devuelve_null()
    {
        await using var db = NewDb(nameof(Una_clave_inexistente_devuelve_null));
        var sut = NewSettingsService(db);

        Assert.Null(await sut.GetValueAsync("No.Existe"));
    }

    [Fact]
    public async Task Un_valor_vacio_se_lectura_como_null()
    {
        await using var db = NewDb(nameof(Un_valor_vacio_se_lectura_como_null));
        var sut = NewSettingsService(db);

        await sut.SetValueAsync("Sql.ConnectionString.Operational", "");

        Assert.Null(await sut.GetValueAsync("Sql.ConnectionString.Operational"));
    }

    [Fact]
    public async Task GetAll_nunca_expone_el_valor_de_un_secreto()
    {
        await using var db = NewDb(nameof(GetAll_nunca_expone_el_valor_de_un_secreto));
        var sut = NewSettingsService(db);

        await sut.SetValueAsync(SettingsKeys.SqlConfiguration, "Server=x;Password=secreto;");
        await sut.SetValueAsync(SettingsKeys.OperationalSourceName, "VTI / VTech");

        var all = await sut.GetAllAsync();
        var secreto = all.Single(x => x.Key == SettingsKeys.SqlConfiguration);

        Assert.DoesNotContain("secreto", secreto.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("********", secreto.Value);
        Assert.Equal("VTI / VTech", all.Single(x => x.Key == SettingsKeys.OperationalSourceName).Value);
    }
}

public class ConfigExportServiceTests : InfrastructureTestBase
{
    private static ConfigExportService NewService(ConfigurationDbContext db) =>
        new(db, NullLogger<ConfigExportService>.Instance);

    [Fact]
    public async Task Export_omite_los_secretos_y_los_hashes_de_contrasena()
    {
        await using var db = NewDb(nameof(Export_omite_los_secretos_y_los_hashes_de_contrasena));
        var settings = NewSettingsService(db);
        await settings.SetValueAsync(SettingsKeys.SqlConfiguration, "Server=x;Password=secreto;");
        await settings.SetValueAsync(SettingsKeys.OperationalSourceName, "VTI");
        db.Set<AppUser>().Add(new AppUser { Username = "ECCSA", DisplayName = "ECCSA", PasswordHash = "$2a$10$hash", Role = AppRole.Administrador });
        await db.SaveChangesAsync();

        var json = await NewService(db).ExportJsonAsync();

        Assert.DoesNotContain("secreto", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$2a$10$hash", json);
        Assert.Contains("ECCSA", json);                       // el usuario sí viaja, sin hash
        Assert.Contains("VTI", json);
    }

    [Fact]
    public async Task Export_de_un_sistema_vacio_no_falla()
    {
        await using var db = NewDb(nameof(Export_de_un_sistema_vacio_no_falla));

        var json = await NewService(db).ExportJsonAsync();

        Assert.Contains("SXA-RTX Analytics", json);
    }

    [Fact]
    public async Task Import_crea_equipos_tablas_y_ajustes()
    {
        await using var db = NewDb(nameof(Import_crea_equipos_tablas_y_ajustes));
        var json = """
        {
          "ApplicationSettings": [ { "Key": "App.UpdateCheckMinutes", "Value": "15" } ],
          "Equipos": [ { "DeviceId": "PC-0123456789ABCDEF", "Nombre": "Celda 1", "Area": "Produccion", "Tipo": 1 } ],
          "TablasConfig": [ { "TableName": "[dbo].[Registros]", "Tipo": 1 } ]
        }
        """;

        var (ok, msg) = await NewService(db).ImportJsonAsync(json);

        Assert.True(ok, msg);
        var equipo = await db.Set<Equipo>().SingleAsync();
        Assert.Equal("Celda 1", equipo.Nombre);
        Assert.Equal(1, equipo.Tipo);
        Assert.Equal("[dbo].[Registros]", (await db.Set<TablaConfig>().SingleAsync()).TableName);
        Assert.Equal("15", await NewSettingsService(db).GetValueAsync("App.UpdateCheckMinutes"));
    }

    [Fact]
    public async Task Import_no_trae_las_cadenas_de_conexion()
    {
        await using var db = NewDb(nameof(Import_no_trae_las_cadenas_de_conexion));
        var json = """
        {
          "ApplicationSettings": [
            { "Key": "Sql.ConnectionString.Operational", "Value": "Server=otro;Password=secreto;" }
          ]
        }
        """;

        var (ok, msg) = await NewService(db).ImportJsonAsync(json);

        Assert.True(ok, msg);
        Assert.Null(await NewSettingsService(db).GetValueAsync(SettingsKeys.SqlOperational));
        Assert.Contains("omitida", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_crea_los_usuarios_inactivos_y_bloqueados()
    {
        await using var db = NewDb(nameof(Import_crea_los_usuarios_inactivos_y_bloqueados));
        var json = """
        {
          "Users": [
            { "Username": "jperez", "DisplayName": "Juan Perez", "Role": 1, "IsActive": true }
          ]
        }
        """;

        var (ok, msg) = await NewService(db).ImportJsonAsync(json);

        Assert.True(ok, msg);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.Equal("jperez", user.Username);
        Assert.Equal(AppRole.Administrador, user.Role);
        Assert.False(user.IsActive);                          // nunca se activa desde el import
        Assert.NotEqual("JPEREZ", user.PasswordHash);          // ningún hash utilizable
    }

    [Fact]
    public async Task Import_no_pisa_el_password_de_un_usuario_existente()
    {
        await using var db = NewDb(nameof(Import_no_pisa_el_password_de_un_usuario_existente));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "jperez", DisplayName = "Juan", Role = AppRole.Usuario,
            IsActive = true, PasswordHash = "$2a$10$hashReal"
        });
        await db.SaveChangesAsync();

        var (ok, _) = await NewService(db).ImportJsonAsync("""
        { "Users": [ { "Username": "jperez", "DisplayName": "Juan Perez", "Role": 2, "IsActive": false } ] }
        """);

        Assert.True(ok);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.Equal("$2a$10$hashReal", user.PasswordHash);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Import_json_invalido_devuelve_error_amigable()
    {
        await using var db = NewDb(nameof(Import_json_invalido_devuelve_error_amigable));

        var (ok, msg) = await NewService(db).ImportJsonAsync("{ esto no es json ");

        Assert.False(ok);
        Assert.Contains("Error importando", msg, StringComparison.OrdinalIgnoreCase);
    }
}

public class SqlProbeTests
{
    [Theory]
    [InlineData("Server=localhost;Database=X;", 5)]
    [InlineData("Server=localhost;Database=X;Connect Timeout=60", 5)]
    public void Create_acota_el_connect_timeout(string cs, int esperado)
    {
        using var conn = SqlProbe.Create(cs);

        Assert.Equal(esperado, conn.ConnectionString == cs ? esperado : new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(conn.ConnectionString).ConnectTimeout);
        Assert.Equal(SqlProbe.CommandTimeoutSeconds, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(conn.ConnectionString).CommandTimeout);
    }

    [Fact]
    public async Task WithTimeout_se_cancela_al_vencer_el_plazo()
    {
        using var cts = SqlProbe.WithTimeout(CancellationToken.None, seconds: 0);

        await Task.Delay(50);
        Assert.True(cts.IsCancellationRequested);
    }
}