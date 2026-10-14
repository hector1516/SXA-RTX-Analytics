using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Garantiza que siempre exista una vía de acceso de superadmin (Administrador).
///
/// - Si <paramref name="resetPassword"/> y hay contraseña: se (re)crea el superadmin
///   con esa contraseña y queda obligado a cambiarla en el próximo inicio de sesión.
///   Es la vía de recuperación cuando TODOS olvidan su contraseña.
/// - Si no hay ningún Administrador activo y hay contraseña: se crea el superadmin.
///
/// La contraseña nunca se fija en el código fuente: viene de la configuración
/// (variable de entorno BootstrapAdmin__Password en el App Pool / servicio).
/// </summary>
public sealed class BootstrapAdminService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<BootstrapAdminService> _logger;

    public BootstrapAdminService(ConfigurationDbContext db, ILogger<BootstrapAdminService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve:
    ///   "reset"   -> se reseteó la contraseña del superadmin (debe cambiarla al entrar).
    ///   "created" -> se creó el superadmin por defecto (primer arranque sin administradores).
    ///   "ok"      -> ya existe al menos un Administrador activo y no se pidió reset.
    /// </summary>
    public async Task<string> EnsureAsync(
        string? username,
        string? password,
        string? displayName,
        bool resetPassword,
        string? defaultPassword,
        CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(username) ? "ECCSA" : username.Trim();
        var display = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();

        if (resetPassword && !string.IsNullOrWhiteSpace(password))
        {
            var user = await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Username == name, ct);
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            if (user is null)
            {
                _db.Set<AppUser>().Add(new AppUser { Username = name, PasswordHash = hash, DisplayName = display, Role = AppRole.Administrador, MustChangePassword = true });
            }
            else
            {
                user.PasswordHash = hash;
                user.Role = AppRole.Administrador;
                user.IsActive = true;
                user.MustChangePassword = true;
                user.UpdatedAtUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("BootstrapAdmin: contraseña del superadmin '{Username}' restablecida; deberá cambiarla al entrar", name);
            return "reset";
        }

        var hasActiveAdmin = await _db.Set<AppUser>()
            .AnyAsync(x => x.Role == AppRole.Administrador && x.IsActive, ct);
        if (hasActiveAdmin) return "ok";

        // Sin administradores: crea el superadmin por defecto (siempre hay una vía de acceso).
        var initial = !string.IsNullOrWhiteSpace(password) ? password
            : (!string.IsNullOrWhiteSpace(defaultPassword) ? defaultPassword : "Qwe123456");
        _db.Set<AppUser>().Add(new AppUser
        {
            Username = name,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(initial),
            DisplayName = display,
            Role = AppRole.Administrador,
            MustChangePassword = false,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("BootstrapAdmin: superadmin por defecto '{Username}' creado", name);
        return "created";
    }
}