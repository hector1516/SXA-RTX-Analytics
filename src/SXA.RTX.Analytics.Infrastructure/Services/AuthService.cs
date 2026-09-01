using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Application.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<AuthService> _logger;
    public AuthService(ConfigurationDbContext db, ILogger<AuthService> logger) { _db = db; _logger = logger; }

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Username == username, ct);
        if (user is null || !user.IsActive) return new(false, "Usuario no encontrado o inactivo.");
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return new(false, "Contraseña incorrecta.");
        return new(true, "OK", user);
    }

    public async Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
        => await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Username == username, ct);

    public async Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken ct = default)
        => await _db.Set<AppUser>().OrderBy(x => x.Username).ToListAsync(ct);

    public async Task<AuthResult> CreateUserAsync(string username, string password, string displayName, AppRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return new(false, "Usuario y contraseña requeridos.");
        if (await _db.Set<AppUser>().AnyAsync(x => x.Username == username, ct)) return new(false, "Ya existe ese usuario.");
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var u = new AppUser { Username = username.Trim(), PasswordHash = hash, DisplayName = displayName.Trim(), Role = role };
        _db.Set<AppUser>().Add(u);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Usuario creado {Username} rol {Role}", username, role);
        return new(true, "Usuario creado.", u);
    }

    public async Task<AuthResult> UpdateUserAsync(Guid id, string displayName, AppRole role, bool isActive, string? newPassword, CancellationToken ct = default)
    {
        var u = await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return new(false, "No encontrado.");
        u.DisplayName = displayName.Trim();
        u.Role = role;
        u.IsActive = isActive;
        u.UpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(newPassword)) u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync(ct);
        return new(true, "Actualizado.", u);
    }

    public async Task<AuthResult> DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        var u = await _db.Set<AppUser>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return new(false, "No encontrado.");
        // Evitar borrar el último administrador
        if (u.Role == AppRole.Administrador)
        {
            var adminCount = await _db.Set<AppUser>().CountAsync(x => x.Role == AppRole.Administrador && x.Id != id, ct);
            if (adminCount == 0) return new(false, "No puedes borrar el último administrador.");
        }
        _db.Set<AppUser>().Remove(u);
        await _db.SaveChangesAsync(ct);
        return new(true, "Eliminado.");
    }
}
