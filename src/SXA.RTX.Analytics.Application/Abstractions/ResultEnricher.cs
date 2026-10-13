using SXA.RTX.Analytics.Reporting.Models;

namespace SXA.RTX.Analytics.Application.Abstractions;

/// <summary>
/// Enriquece el resultado de una consulta añadiendo NombrePC y TipoMaquina, resueltos
/// desde el catálogo de equipos (DeviceId -> NombrePC/TipoMaquina). Funciona sin tocar
/// la base operacional: se hace en memoria sobre el valor de la columna de origen.
/// </summary>
public static class ResultEnricher
{
    public const string DefaultOriginColumn = "OrigenPC";

    /// <summary>
    /// Devuelve un QueryResult con dos columnas nuevas al final (NombrePC, TipoMaquina)
    /// cuando el resultado contiene la columna de origen y hay catálogo disponible.
    /// </summary>
    public static QueryResult Enrich(
        QueryResult result,
        IReadOnlyList<CatalogDeviceDto> catalog,
        string? originColumn)
    {
        var origin = SanitizeIdentifier(originColumn);
        var originIndex = -1;
        for (var i = 0; i < result.Columns.Count; i++)
            if (string.Equals(result.Columns[i].Name, origin, StringComparison.OrdinalIgnoreCase)) { originIndex = i; break; }
        if (originIndex < 0 || catalog.Count == 0) return result;

        var map = new Dictionary<string, CatalogDeviceDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in catalog)
            if (!string.IsNullOrEmpty(c.DeviceId)) map.TryAdd(c.DeviceId, c);

        var newColumns = result.Columns
            .Append(new QueryColumn("NombrePC", "NombrePC", typeof(string)))
            .Append(new QueryColumn("TipoMaquina", "TipoMaquina", typeof(string)))
            .ToList();

        var newRows = new List<IReadOnlyList<object?>>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var deviceId = originIndex < row.Count ? row[originIndex]?.ToString() : null;
            map.TryGetValue(deviceId ?? "", out var cat);
            var extended = new List<object?>(row.Count + 2);
            extended.AddRange(row);
            extended.Add(cat?.NombrePC);
            extended.Add(cat?.TipoMaquina);
            newRows.Add(extended);
        }

        return new QueryResult
        {
            Columns = newColumns,
            Rows = newRows,
            ExecutionTime = result.ExecutionTime,
            IsTruncated = result.IsTruncated,
        };
    }

    /// <summary>
    /// Valida y normaliza un nombre de columna para meterlo entre corchetes en SQL.
    /// Se elimina todo '[' y ']' (neutraliza inyección) y si queda vacío se usa el nombre por defecto.
    /// </summary>
    public static string SanitizeIdentifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DefaultOriginColumn;
        var s = raw.Trim().Replace("[", "").Replace("]", "").Trim();
        return string.IsNullOrWhiteSpace(s) ? DefaultOriginColumn : s;
    }
}