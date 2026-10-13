using SXA.RTX.Analytics.Domain.Common;

namespace SXA.RTX.Analytics.Domain.Entities;

public enum AppRole
{
    Administrador = 1,
    Usuario = 2
}

public sealed class AppUser : BaseEntity
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string DisplayName { get; set; }
    public AppRole Role { get; set; } = AppRole.Usuario;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// true cuando el usuario debe cambiar su contraseña en el próximo inicio de sesión
    /// (p. ej. tras una recuperación de superadmin). La app lo fuerza a /account/password.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
