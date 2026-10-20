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
    public int Tipo { get; set; }                // 1=VTI 2=VTech,copiado de SXA_PCs.TipoMaquina
    public string? Descripcion { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TablaConfig : BaseEntity
{
    public required string TableName { get; set; } // schema.table

    /// <summary>Fuente/aplicación que escribe la tabla (libre): VTI, Packout, Cajas, Foam, etc.</summary>
    public required string Fuente { get; set; }

    /// <summary>Título amigable de la tabla; null = TableName.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Columna que contiene el número de serie (para trazabilidad). null = no aplica.</summary>
    public string? SerialColumn { get; set; }

    /// <summary>Columna de fecha principal de la tabla. null = autodetectar.</summary>
    public string? DateColumn { get; set; }

    public ICollection<TablaColumna> Columnas { get; set; } = new List<TablaColumna>();
}

/// <summary>
/// Columnas configuradas de una tabla operacional. Las filas marcan las columnas
/// visibles; vacío = mostrar todas. El nickname es solo para mostrar.
/// </summary>
public sealed class TablaColumna : BaseEntity
{
    public Guid TablaConfigId { get; set; }
    public required string ColumnName { get; set; }
    public string? DisplayName { get; set; }
}
