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
                using var conn = SqlProbe.Create(sqlConnectionString);
                await conn.OpenAsync(ct);
                // Intentar leer SXA_PCs; si no existe, cae a fallback
                using var cmd = new SqlCommand("SELECT DeviceId, NombrePC, TipoMaquina, Modelo, UltimoContacto FROM dbo.SXA_PCs ORDER BY UltimoContacto DESC", conn);
                using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<CatalogDeviceDto>();
                while (await r.ReadAsync(ct))
                    list.Add(new CatalogDeviceDto(
                        r.GetString(0),
                        r.IsDBNull(1) ? null : r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2),
                        r.IsDBNull(3) ? null : r.GetString(3),
                        ToDate(r.IsDBNull(4) ? null : r.GetValue(4))));
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
        return equipos.Select(e => new EquipoDto(e.Id, e.DeviceId, e.Nombre, e.Area, e.Tipo, null, null, null, e.IsActive)).ToList();
    }

    public async Task<(bool Success, string Message)> UpsertAsync(string deviceId, string nombre, string area, string? tipoMaquina = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(area))
            return (false, "DeviceId, Nombre y Área son obligatorios.");
        var existing = await _db.Set<Equipo>().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        // Tipo viene de SXA_PCs.TipoMaquina: 1=VTI, 2=VTech, 0=desconocido.
        var tipo = (tipoMaquina ?? "").Trim().ToUpperInvariant() switch
        {
            "VTI" => 1,
            "VTECH" => 2,
            _ => 0,
        };
        if (existing is null)
        {
            _db.Set<Equipo>().Add(new Equipo { DeviceId = deviceId.Trim(), Nombre = nombre.Trim(), Area = area.Trim(), Tipo = tipo });
        }
        else
        {
            existing.Nombre = nombre.Trim();
            existing.Area = area.Trim();
            if (tipo != 0) existing.Tipo = tipo;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return (true, existing is null ? $"Equipo {deviceId} creado." : $"Equipo {deviceId} actualizado.");
    }

    private static DateTime? ToDate(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is DateTime d) return d;
        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null;
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
                using var conn = SqlProbe.Create(sqlConnectionString);
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

    public async Task<IReadOnlyList<TablaConfigDto>> GetConfigAsync(CancellationToken ct = default)
    {
        var all = await _db.Set<TablaConfig>().AsNoTracking()
            .Include(x => x.Columnas)
            .ToListAsync(ct);
        return all
            .OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TablaConfigDto(
                x.TableName,
                x.Fuente ?? "",
                string.IsNullOrWhiteSpace(x.DisplayName) ? null : x.DisplayName,
                string.IsNullOrWhiteSpace(x.SerialColumn) ? null : x.SerialColumn,
                string.IsNullOrWhiteSpace(x.DateColumn) ? null : x.DateColumn,
                x.Columnas
                    .OrderBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new TablaColumnaDto(c.ColumnName, string.IsNullOrWhiteSpace(c.DisplayName) ? null : c.DisplayName))
                    .ToList()))
            .ToList();
    }

    public async Task<(bool Success, string Message)> SaveAsync(IReadOnlyList<TablaConfigDto> selections, CancellationToken ct = default)
    {
        try
        {
            var incoming = selections
                .Where(s => !string.IsNullOrWhiteSpace(s.TableName))
                .GroupBy(s => s.TableName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // Reemplazo completo (borrar y reinsertar): determinista y sin dependencias de
            // rastreo de hijos, que el proveedor InMemory maneja de forma ambigua.
            var oldColumns = await _db.Set<TablaColumna>().ToListAsync(ct);
            if (oldColumns.Count > 0) _db.Set<TablaColumna>().RemoveRange(oldColumns);
            var oldTables = await _db.Set<TablaConfig>().ToListAsync(ct);
            if (oldTables.Count > 0) _db.Set<TablaConfig>().RemoveRange(oldTables);
            await _db.SaveChangesAsync(ct);

            foreach (var sel in incoming)
            {
                var row = new TablaConfig
                {
                    TableName = sel.TableName,
                    Fuente = string.IsNullOrWhiteSpace(sel.Fuente) ? "Sin clasificar" : sel.Fuente.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(sel.DisplayName) ? null : sel.DisplayName.Trim(),
                    SerialColumn = string.IsNullOrWhiteSpace(sel.SerialColumn) ? null : sel.SerialColumn.Trim(),
                    DateColumn = string.IsNullOrWhiteSpace(sel.DateColumn) ? null : sel.DateColumn.Trim(),
                };
                foreach (var col in (sel.Columnas ?? Array.Empty<TablaColumnaDto>())
                             .Where(c => !string.IsNullOrWhiteSpace(c.ColumnName))
                             .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First()))
                    row.Columnas.Add(new TablaColumna
                    {
                        ColumnName = col.ColumnName.Trim(),
                        DisplayName = string.IsNullOrWhiteSpace(col.DisplayName) ? null : col.DisplayName.Trim(),
                    });
                _db.Set<TablaConfig>().Add(row);
            }

            await _db.SaveChangesAsync(ct);
            return (true, $"{incoming.Count} tablas guardadas.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando tablas VTI/VTech");
            return (false, "No se pudo guardar: " + ex.Message);
        }
    }
}
