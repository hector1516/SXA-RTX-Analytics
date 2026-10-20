# Análisis de la base real — Hussmann_Insight

Fuente: dump HeidiSQL de producción (`D:\Antigravity\SXA-RTX\huss_db\huss_db.sql`),
SQL Server 2022, base `Hussmann_Insight`.

## Cómo se cargó localmente

- El dump era **solo datos** (sin `CREATE TABLE`). Se reconstruyó el esquema con las
  52 tablas que tienen datos (todas las columnas como `nvarchar(max)` para garantizar la
  importación) y se cargó en la instancia local `localhost` (SQL Express).
- 12 tablas del dump **no tienen filas ni columnas** en el dump (por eso no se crearon):
  `PackoutAdminIMX`, `PackoutErrIMX`, `PackoutItemsImg`, `PackoutResultadosIMX`,
  `PackoutUsrIMX`, `ParosLinea`, `PcPro`, `sysdiagrams`, `VTi_Groups`, `VTi_ModelXRef`,
  `VTi_SchemaChanges`, `VTi_Users`.
- `VTi_UutRecordDetails` falló en la primera pasada (INSERT multi-fila con tipos mezclados).
  Se recargó fila por fila: **13,098 filas**.

> Importante: los **tipos reales** (fecha/numérico) no vienen en el dump. Para el
> desarrollo sirve; contra la base real la app lee los tipos correctos vía
> `INFORMATION_SCHEMA`. Si se necesita una copia fiel, re-exportar con
> "Estructura + Datos" (HeidiSQL).

## Inventario (filas y fuente sugerida)

| Tabla | Filas | Fuente sugerida |
| --- | ---: | --- |
| report | 2,751,354 | Producción / Proceso |
| PackoutErr | 2,085,164 | Packout |
| workstations_reports | 868,125 | Producción / Estaciones |
| CabecerasErr | 558,798 | Cabeceras |
| CajasEErr | 259,353 | Cajas / Electrónica |
| oven_reports | 222,934 | Producción / Horno |
| toolchange_reports | 174,385 | Producción / Herramental |
| press_reports | 172,133 | Producción / Prensa |
| kuka_reports | 165,760 | Producción / Kuka |
| PackoutResultados | 120,870 | Packout |
| ReportsFoam | 116,899 | Producción / Espuma |
| ReportsCycle | 106,258 | Producción / Ciclos |
| foam_reports | 90,384 | Producción / Espuma |
| CabecerasResultados | 90,496 | Cabeceras |
| nomenclatures | 43,200 | Catálogos |
| log_table | 13,041 | Logs / Línea |
| VTi_UutRecordDetails | 13,098 | VTI |
| Alarms_Report | 4,627 | Alarmas |
| CajasEResultados | 3,228 | Cajas |
| Alarms_Data | 3,200 | Alarmas |
| VTi_UutRecords | 827 | VTI |
| VTi_ManualCmdExecLog | 310 | VTI |
| recipe_table | 201 | Catálogos |
| VTi_ParamChangeLog | 145 | VTI |
| Caseline_Control | 151 | Logs / Línea |
| DG_BOTTOM | 151 | Producción |
| VTi_ModelParameters | 57 | VTI |
| VTi_GroupCommands | 47 | VTI |
| VTi_UutDefects | 20 | VTI |
| VTi_Models | 2 | VTI |
| SXA_PCs | 2 | Equipos |
| Production_Chart | 21 | Producción |
| Production_Data | 20 | Producción |
| inline_table / txt_file_table | 17 / 11 | Logs / Línea |
| Alarms_Config | 102 | Alarmas |
| PackoutResultados/Usr/Admin | 37 / 14 | Packout |
| CabecerasUsr/Admin | 22 / 8 | Cabeceras |
| CajasE usr/admin/catálogos | 1..58 | Cajas |

## Equipos (`SXA_PCs`)

| DeviceId | NombrePC | TipoMaquina | Modelo | Último contacto |
| --- | --- | --- | --- | --- |
| PC-70CE11CB53148920 | IMX | Vti | _ | 2026-10-19 18:00 |
| PC-F1F5F57CAE5BFC13 | IAL-VTI-02 | VTi | _ | 2026-10-19 20:16 |

- Solo hay máquinas VTI hoy (no VTech); el tipo viene con mayúsculas inconsistentes
  ("Vti"/"VTi") → comparar sin distinguir mayúsculas.
- Las tablas `VTi_*` incluyen `OrigenPC` (coincide con lo esperado).

## Trazabilidad por número de serie (base del "timeline")

15 tablas tienen **columna de serie + columna de fecha**:

| Tabla | Serie | Fecha |
| --- | --- | --- |
| report | serialnum | date |
| foam_reports | serial_num | date |
| oven_reports | serial_num | date |
| press_reports | serial_num | date |
| kuka_reports | serial_num | date |
| toolchange_reports | serial_num | date |
| workstations_reports | serial_num | date |
| PackoutResultados | Serie | FechaHora |
| CabecerasResultados | Serie | FechaHora |
| CajasEResultados | Serie | FechaHora |
| Alarms_Report | Serial_Number | Start_Date |
| VTi_UutRecords | SerialNo | DateTime |
| log_table | Serial_Number | Start_Date |
| inline_table | Serial_Number | Start_Date |
| txt_file_table | Serial_Number | Start_Date |

**Validación real** con el serial `MY26L083473`:

| Tabla | Filas |
| --- | ---: |
| report | 17 |
| workstations_reports | 5 |
| oven_reports | 3 |
| press_reports | 2 |
| kuka_reports | 1 |
| toolchange_reports | 1 |
| PackoutResultados | 1 |
| CabecerasResultados | 1 |

Rangos de fecha: `report` 2014-07-24 → 2026-10-19; `VTi_UutRecords`
2026-09-15 → 2026-10-19; `PackoutResultados` 2021-02-17 → 2026-10-19.

## Implicaciones para el diseño

1. El "Tipo" binario VTI/VTech **no alcanza**: cada tabla debe tener una
   **Fuente** libre (VTI, Packout, Cajas, Cabeceras, Producción, Alarmas, Logs,
   Catálogos, Equipos...) que el usuario define y puede ampliar.
2. Los nombres de columnas varían mucho → **nickname por columna** es indispensable.
3. Para el timeline, cada tabla necesita metadatos de **columna serie** y
   **columna fecha** (los nombres no son consistentes).
4. Los históricos son largos (2014→2026) → filtros por rango y paginación obligatorios.
5. Los tipos locales son texto (dump sin estructura); contra la base real la app
   obtiene tipos reales. Para gráficas numéricas conviene validar en la base real.
