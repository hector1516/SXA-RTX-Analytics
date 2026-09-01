using SXA.RTX.Analytics.Domain.Common;

namespace SXA.RTX.Analytics.Domain.Entities;

/// <summary>
/// Mapeo DeviceId (PC-... de SXA-RTX-Sync / DeviceIdentity.cs:148) → Nombre amigable + Área + Tipo traído de SXA_PCs.TipoMaquina.
/// Tabla SXA_RTX_Equipos. Nombre/Area se configuran aquí, Tipo es solo lectura del central.
/// </summary>
public sealed class Equipo : BaseEntity
{
    public required string DeviceId { get; set; } // PC-XXXXXXXXXXXXXXXX, UNIQUE
    public required string Nombre { get; set; }   // asignado en Analytics
    public required string Area { get; set; }
    public string? Descripcion { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TablaConfig : BaseEntity
{
    public required string TableName { get; set; } // schema.table
    public int Tipo { get; set; } // 1=VTI 2=VTech
}
