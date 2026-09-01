using SXA.RTX.Analytics.Domain.Entities;

namespace SXA.RTX.Analytics.Application.Abstractions;

public sealed record AuthResult(bool Success, string Message, AppUser? User = null);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<AuthResult> CreateUserAsync(string username, string password, string displayName, AppRole role, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken ct = default);
    Task<AuthResult> UpdateUserAsync(Guid id, string displayName, AppRole role, bool isActive, string? newPassword, CancellationToken ct = default);
    Task<AuthResult> DeleteUserAsync(Guid id, CancellationToken ct = default);
    Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
}
