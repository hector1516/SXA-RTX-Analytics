namespace SXA.RTX.Analytics.Application.Abstractions;

public interface IConfigurationService
{
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
    Task SetValueAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigurationItemDto>> GetAllAsync(CancellationToken ct = default);
}

public sealed record ConfigurationItemDto(
    Guid Id,
    string Key,
    string Value,
    string? Description,
    string? Category,
    DateTime CreatedAtUtc);
