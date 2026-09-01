using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Domain.Enums;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

public sealed class EquipoService : IEquipoService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<EquipoService> _logger;
    public EquipoService(ConfigurationDbContext db, ILogger<EquipoService> logger) { _db = db; _logger = logger; }

    public async Task<IReadOnlyList<CatalogDeviceDto>> GetCatalogAsync(string? sqlConnectionString, CancellationToken ct = default)
    {
        // Si hay SQL real, lee dbo.SXA_PCs del central de Sync. Si no, simula 4 equipos demo.
        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            try
            {
                using var conn = new SqlConnection(sqlConnectionString);
                await conn.OpenAsync(ct);
                // Intentar leer SXA_PCs; si no existe, cae a fallback
                using var cmd = new SqlCommand("SELECT DeviceId, NombrePC, TipoMaquina, Modelo, UltimoContacto FROM dbo.SXA_PCs ORDER BY UltimoContacto DESC", conn);
                using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<CatalogDeviceDto>();
                while (await r.ReadAsync(ct))
                    list.Add(new CatalogDeviceDto(r.GetString(0), r.IsDBNull(1)?null:r.GetString(1), r.IsDBNull(2)?null:r.GetString(2), r.IsDBNull(3)?null:r.GetString(3), r.IsDBNull(4)?null:r.GetDateTime(4)));
                if (list.Count > 0) return list;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo leer SXA_PCs, usando demo"); }
        }
        // Fallback InMemory/demo + lo que haya en SXA_PCs simulado via InMemory no existe, así que demo
        return new List<CatalogDeviceDto>
        {
            new("PC-A1B2C3D4E5F6A7B8", "VTi-Cell-01", "VTI", "VTI-SmartStation", DateTime.UtcNow.AddMinutes(-5)),
            new("PC-B2C3D4E5F6A7B8C9", "VTi-Cell-02", "VTI", "VTI-SmartStation", DateTime.UtcNow.AddMinutes(-12)),
            new("PC-C3D4E5F6A7B8C9D0", "VTech-Charger-01", "VTech", "VTech-R290", DateTime.UtcNow.AddHours(-1)),
            new("PC-D4E5F6A7B8C9D0E1", "VTech-Charger-02", "VTech", "VTech-R600", DateTime.UtcNow.AddHours(-2)),
        };
    }

    public async Task<IReadOnlyList<EquipoDto>> GetEquiposAsync(CancellationToken ct = default)
    {
        var equipos = await _db.Set<Equipo>().AsNoTracking().ToListAsync(ct);
        // Para Tipo, necesitamos cruzar con catalog. Aquí solo devolvemos sin Tipo, el caller hará merge.
        return equipos.Select(e => new EquipoDto(e.Id, e.DeviceId, e.Nombre, e.Area, null, null, null, e.IsActive)).ToList();
    }

    public async Task<(bool Success, string Message)> UpsertAsync(string deviceId, string nombre, string area, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(area))
            return (false, "DeviceId, Nombre y Área son obligatorios.");
        var existing = await _db.Set<Equipo>().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (existing is null)
        {
            _db.Set<Equipo>().Add(new Equipo { DeviceId = deviceId.Trim(), Nombre = nombre.Trim(), Area = area.Trim() });
        }
        else
        {
            existing.Nombre = nombre.Trim();
            existing.Area = area.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return (true, existing is null ? $"Equipo {deviceId} creado." : $"Equipo {deviceId} actualizado.");
    }
}

public sealed class TablasConfigService : ITablasConfigService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<TablasConfigService> _logger;
    public TablasConfigService(ConfigurationDbContext db, ILogger<TablasConfigService> logger) { _db = db; _logger = logger; }

    public async Task<IReadOnlyList<string>> ListAllTablesAsync(string? sqlConnectionString, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            try
            {
                using var conn = new SqlConnection(sqlConnectionString);
                await conn.OpenAsync(ct);
                using var cmd = new SqlCommand("SELECT QUOTENAME(TABLE_SCHEMA)+'.'+QUOTENAME(TABLE_NAME) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_SCHEMA='dbo' ORDER BY TABLE_NAME", conn);
                using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<string>();
                while (await r.ReadAsync(ct)) list.Add(r.GetString(0));
                if (list.Count > 0) return list;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo listar tablas"); }
        }
        return new List<string> { "[dbo].[SXA_RTX_ApplicationSettings]", "[dbo].[SXA_RTX_AuditLogs]", "[dbo].[Registros]", "[dbo].[VTi_SmartStation_Log]", "[dbo].[VTech_Charging_Log]" };
    }

    public async Task<IReadOnlyList<(string TableName, EquipoTipo Tipo)>> GetConfigAsync(CancellationToken ct = default)
    {
        var all = await _db.Set<TablaConfig>().AsNoTracking().ToListAsync(ct);
        return all.Select(x => (x.TableName, (EquipoTipo)x.Tipo)).ToList();
    }

    public async Task<(bool Success, string Message)> SaveAsync(IReadOnlyList<(string TableName, EquipoTipo Tipo)> selections, CancellationToken ct = default)
    {
        var existing = await _db.Set<TablaConfig>().ToListAsync(ct);
        _db.Set<TablaConfig>().RemoveRange(existing);
        foreach (var (t, tipo) in selections)
            _db.Set<TablaConfig>().Add(new TablaConfig { TableName = t, Tipo = (int)tipo });
        await _db.SaveChangesAsync(ct);
        return (true, $"{selections.Count} tablas guardadas.");
    }
}
