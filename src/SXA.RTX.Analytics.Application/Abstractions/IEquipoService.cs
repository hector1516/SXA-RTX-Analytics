using SXA.RTX.Analytics.Domain.Enums;

namespace SXA.RTX.Analytics.Application.Abstractions;

public sealed record EquipoDto(
    Guid Id,
    string DeviceId,
    string Nombre,
    string Area,
    int Tipo, // persistido: 1=VTI, 2=VTech, 0=desconocido (copiado de SXA_PCs.TipoMaquina)
    EquipoTipo? TipoSync, // leído en vivo del catálogo SXA_PCs, solo lectura
    string? NombrePCSync,
    DateTime? UltimoContacto,
    bool IsActive);

public sealed record CatalogDeviceDto(
    string DeviceId,
    string? NombrePC,
    string? TipoMaquina,
    string? Modelo,
    DateTime? UltimoContacto);

public interface IEquipoService
{
    Task<IReadOnlyList<CatalogDeviceDto>> GetCatalogAsync(string? sqlConnectionString, CancellationToken ct = default);
    Task<IReadOnlyList<EquipoDto>> GetEquiposAsync(CancellationToken ct = default);
    Task<(bool Success, string Message)> UpsertAsync(string deviceId, string nombre, string area, string? tipoMaquina = null, CancellationToken ct = default);
}

public interface ITablasConfigService
{
    Task<IReadOnlyList<string>> ListAllTablesAsync(string? sqlConnectionString, CancellationToken ct = default);
    Task<IReadOnlyList<TablaConfigDto>> GetConfigAsync(CancellationToken ct = default);
    Task<(bool Success, string Message)> SaveAsync(IReadOnlyList<TablaConfigDto> selections, CancellationToken ct = default);
}

/// <summary>
/// Configuración visual de una tabla operacional: Tipo (VTI/VTech), título amigable
/// (DisplayName; null = mostrar el nombre SQL) y columnas visibles (vacío = todas).
/// </summary>
public sealed record TablaConfigDto(
    string TableName,
    EquipoTipo Tipo,
    string? DisplayName,
    IReadOnlyList<string> VisibleColumns);
