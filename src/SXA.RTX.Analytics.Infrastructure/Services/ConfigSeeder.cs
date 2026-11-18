using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Semilla inicial de la configuración (SQLite local) para que la app sea utilizable
/// desde el primer arranque. Nunca escribe en la base SQL de producción.
/// El administrador puede editar/eliminar estas entradas en /configuration.
/// Solo actúa si no hay ninguna tabla configurada.
/// </summary>
public sealed class ConfigSeeder
{
    private readonly ConfigurationDbContext _db;
    private readonly ILogger<ConfigSeeder> _logger;

    public ConfigSeeder(ConfigurationDbContext db, ILogger<ConfigSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _db.Set<TablaConfig>().AnyAsync(ct)) return;

        var tablas = new (string Table, string Fuente, string? Title, string? Serial, string? Date, (string? Table, string? Fk, string? Pk, string? Date, string? Title, string? Result, string? Value, string? Units)? Detail)[]
        {
            ("[dbo].[report]",               "Producción", "Reporte de proceso",    "serialnum",     "date",       null),
            ("[dbo].[foam_reports]",         "Producción", "Espuma (foam)",         "serial_num",    "date",       null),
            ("[dbo].[oven_reports]",         "Producción", "Horno",                 "serial_num",    "date",       null),
            ("[dbo].[press_reports]",        "Producción", "Prensa",                "serial_num",    "date",       null),
            ("[dbo].[kuka_reports]",         "Producción", "Robot Kuka",            "serial_num",    "date",       null),
            ("[dbo].[toolchange_reports]",   "Producción", "Cambio de herramental", "serial_num",    "date",       null),
            ("[dbo].[workstations_reports]", "Producción", "Estaciones de trabajo", "serial_num",    "date",       null),
            ("[dbo].[PackoutResultados]",    "Packout",    "Resultados de packout", "Serie",         "FechaHora",  null),
            ("[dbo].[CabecerasResultados]",  "Cabeceras",  "Resultados de cabecera","Serie",         "FechaHora",  null),
            ("[dbo].[CajasEResultados]",     "Cajas",      "Resultados de cajas",   "Serie",         "FechaHora",  null),
            ("[dbo].[Alarms_Report]",        "Alarmas",    "Reporte de alarmas",    "Serial_Number", "Start_Date", null),
            ("[dbo].[VTi_UutRecords]",       "VTI",        "Pruebas VTI",           "SerialNo",      "DateTime",
                ("dbo.VTi_UutRecordDetails", "UutRecordID", "ID", "DateTime", "Test", "Result", "Value", "Units")),
            ("[dbo].[VTech_ProductionTrace]", "VTech",      "Trazabilidad VTech",    "Serial",        null,       null),
            ("[dbo].[VTech_CircuitsResult]",  "VTech",      "Resultados de circuitos VTech", "CircuitBarcode", "DateTime", null),
            ("[dbo].[Alarms_Data]",           "Alarmas",    "Datos de alarmas",          null,            null,       null),
            ("[dbo].[PackoutErr]",           "Packout",    "Errores de packout",        null,            null,       null),
            ("[dbo].[CabecerasErr]",         "Cabeceras",  "Errores de cabecera",       null,            null,       null),
            ("[dbo].[CajasEErr]",            "Cajas",      "Errores de cajas",          null,            null,       null),
            ("[dbo].[Caseline_Control]",     "Producción", "Control de línea",          null,            null,       null),
            ("[dbo].[DG_BOTTOM]",            "Producción", "DG Bottom",                 null,            null,       null),
            ("[dbo].[Production_Chart]",     "Producción", "Gráfica de producción",    null,            null,       null),
            ("[dbo].[Production_Data]",      "Producción", "Datos de producción",       null,            null,       null),
            ("[dbo].[nomenclatures]",       "Catálogos",  "Nomenclaturas",             null,            null,       null),
            ("[dbo].[recipe_table]",         "Catálogos",  "Recetas",                   null,            null,       null),
            ("[dbo].[VTi_UutDefects]",      "VTI",        "Defectos VTI",              null,            null,       null),
            ("[dbo].[VTi_Models]",          "VTI",        "Modelos VTI",               null,            null,       null),
            ("[dbo].[VTi_ModelParameters]", "VTI",        "Parámetros VTI",            null,            null,       null),
            ("[dbo].[VTech_ChargeRecipies]", "VTech",     "Recetas de carga VTech",    null,            null,       null),
            ("[dbo].[VTech_VacuumRecipies]", "VTech",     "Recetas de vacío VTech",    null,            null,       null),
            ("[dbo].[VTech_LeakPoints]",    "VTech",      "Puntos de fuga VTech",      null,            null,       null),
            ("[dbo].[VTech_VacuumPoint]",   "VTech",      "Puntos de vacío VTech",     null,            null,       null),
            ("[dbo].[VTech_Recipies]",      "VTech",      "Recetas VTech",             null,            null,       null),
            ("[dbo].[log_table]",           "Producción", "Log de línea",              "Serial_Number", "Start_Date", null),
            ("[dbo].[inline_table]",        "Producción", "Log inline",                "Serial_Number", "Start_Date", null),
            ("[dbo].[txt_file_table]",      "Producción", "Log de archivo",            "Serial_Number", "Start_Date", null),
        };

        foreach (var t in tablas)
        {
            var row = new TablaConfig
            {
                TableName = t.Table,
                Fuente = t.Fuente,
                DisplayName = t.Title,
                SerialColumn = t.Serial,
                DateColumn = t.Date,
                DetailTable = t.Detail?.Table,
                DetailForeignKey = t.Detail?.Fk,
                DetailParentKey = t.Detail?.Pk,
                DetailDateColumn = t.Detail?.Date,
                DetailTitleColumn = t.Detail?.Title,
                DetailResultColumn = t.Detail?.Result,
                DetailValueColumn = t.Detail?.Value,
                DetailUnitsColumn = t.Detail?.Units,
            };
            _db.Set<TablaConfig>().Add(row);
        }
        await _db.SaveChangesAsync(ct);

        // Nicknames útiles (solo visual).
        var nicknames = new (string Table, string Col, string Nick)[]
        {
            ("[dbo].[report]", "serialnum", "Serie"),
            ("[dbo].[report]", "date", "Fecha"),
            ("[dbo].[report]", "Nomenclature", "Nomenclatura"),
            ("[dbo].[report]", "station", "Estación"),
            ("[dbo].[report]", "temp_mp", "Temp MP"),
            ("[dbo].[report]", "cycle_time", "Tiempo de ciclo"),
            ("[dbo].[report]", "status", "Estado"),
            ("[dbo].[VTi_UutRecords]", "SerialNo", "Serie"),
            ("[dbo].[VTi_UutRecords]", "DateTime", "Fecha"),
            ("[dbo].[VTi_UutRecords]", "ModelNo", "Modelo"),
            ("[dbo].[VTi_UutRecords]", "TestType", "Tipo de prueba"),
            ("[dbo].[VTi_UutRecords]", "TestResult", "Resultado"),
            ("[dbo].[VTi_UutRecords]", "OrigenPC", "Equipo"),
            ("[dbo].[VTech_ProductionTrace]", "Serial", "Serie"),
            ("[dbo].[VTech_ProductionTrace]", "Model", "Modelo"),
            ("[dbo].[VTech_ProductionTrace]", "NCircuits", "Nº Circuitos"),
            ("[dbo].[VTech_ProductionTrace]", "Integrity", "Integridad"),
            ("[dbo].[VTech_ProductionTrace]", "OrigenPC", "Equipo"),
            ("[dbo].[VTech_CircuitsResult]", "CircuitBarcode", "Código de circuito"),
            ("[dbo].[VTech_CircuitsResult]", "DateTime", "Fecha"),
            ("[dbo].[VTech_CircuitsResult]", "VacResult", "Resultado vacío"),
            ("[dbo].[VTech_CircuitsResult]", "ChargeResult", "Resultado carga"),
            ("[dbo].[VTech_CircuitsResult]", "LeakResult", "Resultado fuga"),
            ("[dbo].[VTech_CircuitsResult]", "VacLevel", "Nivel vacío"),
            ("[dbo].[VTech_CircuitsResult]", "LeakLevel", "Nivel fuga"),
            ("[dbo].[VTech_CircuitsResult]", "Quantity", "Cantidad"),
            ("[dbo].[VTech_CircuitsResult]", "Side", "Lado"),
            ("[dbo].[VTech_CircuitsResult]", "OrigenPC", "Equipo"),
            ("[dbo].[PackoutResultados]", "Serie", "Serie"),
            ("[dbo].[PackoutResultados]", "FechaHora", "Fecha"),
            ("[dbo].[PackoutResultados]", "Resultado", "Resultado"),
            ("[dbo].[PackoutResultados]", "Operador", "Operador"),
            ("[dbo].[log_table]", "Serial_Number", "Serie"),
            ("[dbo].[log_table]", "Start_Date", "Fecha"),
            ("[dbo].[log_table]", "Station", "Estación"),
            ("[dbo].[inline_table]", "Serial_Number", "Serie"),
            ("[dbo].[inline_table]", "Start_Date", "Fecha"),
            ("[dbo].[txt_file_table]", "Serial_Number", "Serie"),
            ("[dbo].[txt_file_table]", "Start_Date", "Fecha"),
        };

        var byTable = await _db.Set<TablaConfig>().ToDictionaryAsync(x => x.TableName, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var n in nicknames)
        {
            if (!byTable.TryGetValue(n.Table, out var cfg)) continue;
            _db.Set<TablaColumna>().Add(new TablaColumna
            {
                TablaConfigId = cfg.Id,
                ColumnName = n.Col,
                DisplayName = n.Nick,
            });
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("ConfigSeeder: configuración inicial creada ({Tables} tablas).", tablas.Length);
    }
}