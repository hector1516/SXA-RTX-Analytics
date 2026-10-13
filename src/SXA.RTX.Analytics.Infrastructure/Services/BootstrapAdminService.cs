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
    ///   "created" -> se creó el superadmin (debe cambiar la contraseña al entrar).
    ///   "ok"      -> ya existe al menos un Administrador activo y no se pidió reset.
    ///   "none"    -> no hay administrador y no se proporcionó contraseña (lo decide el llamador).
    /// </summary>
    public async Task<string> EnsureAsync(
        string? username,
        string? password,
        string? displayName,
        bool resetPassword,
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

        if (!string.IsNullOrWhiteSpace(password))
        {
            _db.Set<AppUser>().Add(new AppUser
            {
                Username = name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = display,
                Role = AppRole.Administrador,
                MustChangePassword = true,
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("BootstrapAdmin: superadmin '{Username}' creado (debe cambiar la contraseña al entrar)", name);
            return "created";
        }

        return "none";
    }

    /// <summary>Semilla de desarrollo (solo entorno local): ECCSA / Qwe123456.</summary>
    public async Task CreateDevDefaultAsync(CancellationToken ct = default)
    {
        if (await _db.Set<AppUser>().AnyAsync(x => x.Username == "ECCSA", ct)) return;
        _db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Qwe123456"),
            DisplayName = "ECCSA",
            Role = AppRole.Administrador,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seed desarrollo ECCSA creado");
    }
}