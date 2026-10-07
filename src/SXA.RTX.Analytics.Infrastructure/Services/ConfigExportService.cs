using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Exporta e importa la configuración a JSON para migrarla entre equipos.
/// Reglas de seguridad:
/// - Los secretos (valores marcados como cifrados, p. ej. cadenas de conexión) NUNCA se exportan.
/// - Los hashes de contraseña NUNCA se exportan. Los usuarios importados quedan inactivos y
///   con una contraseña bloqueada: el administrador debe asignarles una nueva.
/// </summary>
public sealed class ConfigExportService : IConfigExportService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<ConfigExportService> _logger;

    public ConfigExportService(ConfigurationDbContext db, ILogger<ConfigExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Hash que nunca corresponde a una contraseña válida (impide el acceso accidental).</summary>
    private const string LockedPasswordHash = "!locked-by-config-import!";

    public async Task<string> ExportJsonAsync(CancellationToken ct = default)
    {
        var settings = await _db.Set<ApplicationSetting>().AsNoTracking().ToListAsync(ct);
        var equipos = await _db.Set<Equipo>().AsNoTracking()
            .Select(e => new { e.DeviceId, e.Nombre, e.Area, e.Tipo, e.Descripcion, e.IsActive })
            .ToListAsync(ct);
        var tablas = await _db.Set<TablaConfig>().AsNoTracking()
            .Select(t => new { t.TableName, t.Tipo })
            .ToListAsync(ct);
        var usuarios = await _db.Set<AppUser>().AsNoTracking()
            .Select(u => new { u.Username, u.DisplayName, u.Role, u.IsActive, u.CreatedAtUtc })
            .ToListAsync(ct);

        var data = new
        {
            ExportedAtUtc = DateTime.UtcNow,
            Version = 2,
            App = "SXA-RTX Analytics",
            // Se omiten los secretos: las conexiones se reconfiguran en /configuration del destino.
            ApplicationSettings = settings.Where(s => !s.IsEncrypted)
                .Select(s => new { s.Key, s.Value, s.Description, s.Category }).ToList(),
            OmittedSecrets = settings.Count(s => s.IsEncrypted),
            Equipos = equipos,
            TablasConfig = tablas,
            // Sin PasswordHash a propósito.
            Users = usuarios,
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<(bool Success, string Message)> ImportJsonAsync(string json, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var equipos = 0; var tablas = 0; var settings = 0; var usuarios = 0; var secretosOmitidos = 0;

            if (root.TryGetProperty("Equipos", out var equiposEl) && equiposEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in equiposEl.EnumerateArray())
                {
                    var deviceId = el.GetProperty("DeviceId").GetString();
                    if (string.IsNullOrWhiteSpace(deviceId)) continue;
                    var nombre = el.TryGetProperty("Nombre", out var n) ? n.GetString() ?? "" : "";
                    var area = el.TryGetProperty("Area", out var a) ? a.GetString() ?? "" : "";
                    var tipo = el.TryGetProperty("Tipo", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                    var existing = await _db.Set<Equipo>().FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
                    if (existing is null)
                    {
                        _db.Set<Equipo>().Add(new Equipo
                        {
                            DeviceId = deviceId,
                            Nombre = nombre,
                            Area = area,
                            Tipo = tipo,
                            Descripcion = el.TryGetProperty("Descripcion", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                            IsActive = el.TryGetProperty("IsActive", out var act) && act.ValueKind == JsonValueKind.True
                        });
                    }
                    else
                    {
                        existing.Nombre = nombre;
                        existing.Area = area;
                        if (tipo != 0) existing.Tipo = tipo;
                        if (el.TryGetProperty("Descripcion", out var de) && de.ValueKind == JsonValueKind.String) existing.Descripcion = de.GetString();
                    }
                    equipos++;
                }
            }

            if (root.TryGetProperty("TablasConfig", out var tablasEl) && tablasEl.ValueKind == JsonValueKind.Array)
            {
                var actuales = await _db.Set<TablaConfig>().ToListAsync(ct);
                _db.Set<TablaConfig>().RemoveRange(actuales);
                foreach (var el in tablasEl.EnumerateArray())
                {
                    var tableName = el.TryGetProperty("TableName", out var tn) ? tn.GetString() : null;
                    if (string.IsNullOrWhiteSpace(tableName)) continue;
                    _db.Set<TablaConfig>().Add(new TablaConfig
                    {
                        TableName = tableName,
                        Tipo = el.TryGetProperty("Tipo", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 1
                    });
                    tablas++;
                }
            }

            if (root.TryGetProperty("ApplicationSettings", out var settingsEl) && settingsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in settingsEl.EnumerateArray())
                {
                    var key = el.TryGetProperty("Key", out var k) ? k.GetString() : null;
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    // No se importan claves de conexiones: se reconfiguran a mano en el destino.
                    if (key.StartsWith("Sql.ConnectionString.", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("Odbc.ConnectionString.", StringComparison.OrdinalIgnoreCase))
                    { secretosOmitidos++; continue; }
                    var val = el.TryGetProperty("Value", out var v) ? v.GetString() ?? "" : "";
                    var existing = await _db.Set<ApplicationSetting>().FirstOrDefaultAsync(x => x.Key == key, ct);
                    if (existing is null)
                        _db.Set<ApplicationSetting>().Add(new ApplicationSetting
                        {
                            Key = key,
                            Value = val,
                            Description = el.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                            Category = el.TryGetProperty("Category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
                        });
                    else
                    {
                        existing.Value = val;
                        existing.IsEncrypted = false;   // el valor importado viene en claro
                    }
                    settings++;
                }
            }

            if (root.TryGetProperty("Users", out var usersEl) && usersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in usersEl.EnumerateArray())
                {
                    var username = el.TryGetProperty("Username", out var un) ? un.GetString() : null;
                    if (string.IsNullOrWhiteSpace(username)) continue;
                    var displayName = el.TryGetProperty("DisplayName", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString() ?? username : username;
                    var role = el.TryGetProperty("Role", out var r) && r.ValueKind == JsonValueKind.Number ? (AppRole)r.GetInt32() : AppRole.Usuario;
                    var existing = await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Username == username, ct);
                    if (existing is null)
                    {
                        // Sin hash de contraseña no se puede crear una cuenta utilizable: se crea
                        // inactiva y bloqueada para que un administrador le asigne una contraseña.
                        _db.Set<AppUser>().Add(new AppUser
                        {
                            Username = username,
                            DisplayName = displayName,
                            Role = role,
                            IsActive = false,
                            PasswordHash = LockedPasswordHash,
                        });
                        usuarios++;
                    }
                    else
                    {
                        existing.DisplayName = displayName;
                        existing.Role = role;
                        // El estado activo del origen NO se aplica: solo un administrador decide.
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            var partes = new List<string>
            {
                $"{equipos} equipo(s)",
                $"{tablas} tabla(s)",
                $"{settings} ajuste(s)",
            };
            if (usuarios > 0) partes.Add($"{usuarios} usuario(s) inactivos con contraseña pendiente");
            if (secretosOmitidos > 0) partes.Add($"{secretosOmitidos} cadena(s) de conexión omitida(s): configúralas en /configuration");
            return (true, "Configuración importada: " + string.Join(", ", partes) + ".");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error importando configuracion");
            return (false, $"Error importando: {ex.Message}");
        }
    }
}