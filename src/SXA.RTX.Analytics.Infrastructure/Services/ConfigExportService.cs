using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

public sealed class ConfigExportService : IConfigExportService
{
    private readonly ConfigurationDbContext _db;
    public ConfigExportService(ConfigurationDbContext db) => _db = db;

    public async Task<string> ExportJsonAsync(CancellationToken ct = default)
    {
        var data = new
        {
            ExportedAtUtc = DateTime.UtcNow,
            Version = 1,
            App = "SXA-RTX Analytics",
            ApplicationSettings = await _db.Set<ApplicationSetting>().AsNoTracking().ToListAsync(ct),
            Equipos = await _db.Set<Equipo>().AsNoTracking().ToListAsync(ct),
            TablasConfig = await _db.Set<TablaConfig>().AsNoTracking().ToListAsync(ct),
            Users = await _db.Set<AppUser>().AsNoTracking().Select(u => new { u.Id, u.Username, u.DisplayName, u.Role, u.IsActive, u.CreatedAtUtc }).ToListAsync(ct),
            // Cada vez que agregues nueva config, añádela aquí
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<(bool Success, string Message)> ImportJsonAsync(string json, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // Por simplicidad: importa Equipos y TablasConfig y ApplicationSettings (merge por clave/DeviceId/TableName)
            if (doc.RootElement.TryGetProperty("Equipos", out var equiposEl))
            {
                foreach (var el in equiposEl.EnumerateArray())
                {
                    var deviceId = el.GetProperty("DeviceId").GetString()!;
                    var existing = await _db.Set<Equipo>().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
                    if (existing is null)
                    {
                        _db.Set<Equipo>().Add(new Equipo
                        {
                            DeviceId = deviceId,
                            Nombre = el.GetProperty("Nombre").GetString()!,
                            Area = el.GetProperty("Area").GetString()!,
                            Descripcion = el.TryGetProperty("Descripcion", out var d) ? d.GetString() : null,
                            IsActive = el.TryGetProperty("IsActive", out var a) ? a.GetBoolean() : true
                        });
                    }
                    else
                    {
                        existing.Nombre = el.GetProperty("Nombre").GetString()!;
                        existing.Area = el.GetProperty("Area").GetString()!;
                    }
                }
            }
            if (doc.RootElement.TryGetProperty("TablasConfig", out var tablasEl))
            {
                var existing = await _db.Set<TablaConfig>().ToListAsync(ct);
                _db.Set<TablaConfig>().RemoveRange(existing);
                foreach (var el in tablasEl.EnumerateArray())
                {
                    _db.Set<TablaConfig>().Add(new TablaConfig
                    {
                        TableName = el.GetProperty("TableName").GetString()!,
                        Tipo = el.GetProperty("Tipo").GetInt32()
                    });
                }
            }
            if (doc.RootElement.TryGetProperty("ApplicationSettings", out var settingsEl))
            {
                foreach (var el in settingsEl.EnumerateArray())
                {
                    var key = el.GetProperty("Key").GetString()!;
                    var val = el.GetProperty("Value").GetString()!;
                    var existing = await _db.Set<ApplicationSetting>().FirstOrDefaultAsync(x => x.Key == key, ct);
                    if (existing is null) _db.Set<ApplicationSetting>().Add(new ApplicationSetting { Key = key, Value = val, Description = el.TryGetProperty("Description", out var d) ? d.GetString() : null, Category = el.TryGetProperty("Category", out var c) ? c.GetString() : null });
                    else existing.Value = val;
                }
            }
            await _db.SaveChangesAsync(ct);
            return (true, "Configuración importada correctamente.");
        }
        catch (Exception ex) { return (false, $"Error importando: {ex.Message}"); }
    }
}
