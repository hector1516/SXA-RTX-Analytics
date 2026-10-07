param(
  [string]$Repo = "D:\Antigravity\SXA-RTX\SXA-RTX-Analytics",
  [string]$Version = "1.1.1"
)
$ErrorActionPreference = "Stop"

$OutDir = Join-Path $Repo "docs\report"
$Shots = Join-Path $OutDir "screens"
$Base   = Join-Path $OutDir "SXA-RTX-Analytics-Informe-v$Version"

# ---------------------------------------------------------------- helpers ----
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$doc = $word.Documents.Add()

# A4 vertical, margenes 2.2 cm
$doc.PageSetup.PageWidth  = $word.CentimetersToPoints(21)
$doc.PageSetup.PageHeight = $word.CentimetersToPoints(29.7)
$doc.PageSetup.TopMargin    = $word.CentimetersToPoints(2.2)
$doc.PageSetup.BottomMargin = $word.CentimetersToPoints(2.2)
$doc.PageSetup.LeftMargin   = $word.CentimetersToPoints(2.2)
$doc.PageSetup.RightMargin  = $word.CentimetersToPoints(2.2)

$sel = $word.Selection
$CONTENT_W = 21 - 4.4   # cm utiles
$MAX_H     = 20.0        # cm maximos por imagen

function Go([int]$page) { $script:sel.GoTo(1, 1, $page) | Out-Null }

function H1([string]$t) {
  $sel.Style = $doc.Styles.Item("Heading 1"); $sel.TypeText($t); $sel.TypeParagraph()
  $sel.Style = $doc.Styles.Item("Normal")
}
function H2([string]$t) {
  $sel.Style = $doc.Styles.Item("Heading 2"); $sel.TypeText($t); $sel.TypeParagraph()
  $sel.Style = $doc.Styles.Item("Normal")
}
function H3([string]$t) {
  $sel.Style = $doc.Styles.Item("Heading 3"); $sel.TypeText($t); $sel.TypeParagraph()
  $sel.Style = $doc.Styles.Item("Normal")
}
function P([string]$t, [switch]$Bold, [switch]$Italic, [int]$Size = 0) {
  if ($Bold) { $sel.Font.Bold = 1 }
  if ($Italic) { $sel.Font.Italic = 1 }
  if ($Size -gt 0) { $sel.Font.Size = $Size }
  $sel.TypeText($t)
  $sel.TypeParagraph()
  $sel.Font.Bold = 0; $sel.Font.Italic = 0; $sel.Font.Size = 11
}
function Bullet([string]$t, [int]$level = 0) {
  $sel.Style = $doc.Styles.Item("List Paragraph")
  $sel.ParagraphFormat.LeftIndent = $word.CentimetersToPoints(0.6 + 0.6 * $level)
  $sel.ParagraphFormat.SpaceAfter = 2
  $sel.TypeText([char]0x2022 + "  " + $t)
  $sel.TypeParagraph()
  $sel.Style = $doc.Styles.Item("Normal")
  $sel.ParagraphFormat.LeftIndent = 0
  $sel.ParagraphFormat.SpaceAfter = 6
}
function Mono([string]$t) {
  $sel.Font.Name = "Consolas"; $sel.Font.Size = 9
  $sel.TypeText($t); $sel.TypeParagraph()
  $sel.Font.Name = "Calibri"; $sel.Font.Size = 11
}
function Code([string[]]$lines) {
  $sel.Style = $doc.Styles.Item("Normal")
  $sel.ParagraphFormat.LeftIndent = $word.CentimetersToPoints(0.5)
  $sel.ParagraphFormat.SpaceAfter = 0
  $sel.Font.Name = "Consolas"; $sel.Font.Size = 8.5
  foreach ($l in $lines) { $sel.TypeText($l); $sel.TypeParagraph() }
  $sel.Font.Name = "Calibri"; $sel.Font.Size = 11
  $sel.ParagraphFormat.LeftIndent = 0
  $sel.ParagraphFormat.SpaceAfter = 6
}
function Shot([string]$file, [string]$caption) {
  $p = Join-Path $Shots $file
  if (-not (Test-Path $p)) { Write-Host "  (falta $file, se omite)"; return }
  $w = $word.CentimetersToPoints($CONTENT_W)
  $maxH = $word.CentimetersToPoints($MAX_H)
  $pic = $sel.InlineShapes.AddPicture($p, $false, $true)
  $ratio = $pic.Height / $pic.Width
  $pic.LockAspectRatio = -1     # msoTrue
  $pic.Width = $w
  if ($pic.Height -gt $maxH) { $pic.Height = $maxH }
  $sel.TypeParagraph()
  if ($caption) {
    $sel.ParagraphFormat.Alignment = 1
    P $caption -Italic -Size 9
    $sel.ParagraphFormat.Alignment = 0
  }
  $sel.ParagraphFormat.SpaceAfter = 10
}
function Table([string[]]$headers, [object[]]$rows) {
  $r = $rows.Count + 1; $c = $headers.Count
  $t = $doc.Tables.Add($sel.Range, $r, $c)
  $t.Borders.InsideLineStyle = 1
  $t.Borders.OutsideLineStyle = 1
  $t.Range.Font.Size = 9.5
  for ($i = 0; $i -lt $c; $i++) {
    $t.Cell(1, $i + 1).Range.Text = $headers[$i]
    $t.Cell(1, $i + 1).Range.Font.Bold = 1
    $t.Cell(1, $i + 1).Shading.BackgroundPatternColor = 15921906
  }
  for ($i = 0; $i -lt $rows.Count; $i++) {
    for ($j = 0; $j -lt $c; $j++) {
      $t.Cell($i + 2, $j + 1).Range.Text = $rows[$i][$j]
    }
  }
  $t.Columns.AutoFit()
  $sel.EndKey(6) | Out-Null     # wdStory
  $sel.TypeParagraph()
}
function BreakPage() { $sel.InsertBreak(7) | Out-Null }   # wdPageBreak

# ------------------------------------------------------------ portada -------
$sel.ParagraphFormat.Alignment = 1
$sel.Font.Size = 34; $sel.Font.Bold = 1
$sel.TypeText("SXA-RTX Analytics")
$sel.TypeParagraph()
$sel.Font.Size = 15; $sel.Font.Bold = 0
$sel.TypeText("Plataforma interna de reporte y consulta operacional")
$sel.TypeParagraph()
$sel.TypeParagraph()
$sel.Font.Size = 12
$sel.TypeText("Informe de avance funcional, despliegue y operación")
$sel.TypeParagraph()
$sel.TypeParagraph()
$sel.Font.Size = 11
$sel.TypeText("Versión del documento: $Version")
$sel.TypeParagraph()
$sel.TypeText("Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$sel.TypeParagraph()
$sel.TypeText("Clasificación: Uso interno ECCSA Automation")
$sel.TypeParagraph()
$sel.TypeParagraph()
$sel.Font.Size = 10; $sel.Font.Italic = 1
$sel.TypeText("Repositorio: https://github.com/hector1516/SXA-RTX-Analytics")
$sel.TypeParagraph()
$sel.TypeText("Release: https://github.com/hector1516/SXA-RTX-Analytics/releases/tag/v$Version")
$sel.ParagraphFormat.Alignment = 0
$sel.Font.Italic = 0; $sel.Font.Size = 11

BreakPage

# ------------------------------------------------------ 1. Resumen ---------
H1 "1. Resumen ejecutivo"
P "SXA-RTX Analytics es la plataforma interna de ECCSA Automation para consultar, visualizar y exportar datos operacionales de las plantas VTI y VTech. Reemplaza los Crystals Reports estáticos por una aplicación web con consulta dinámica, visualizaciones y exportación a Excel y PDF."
P "El sistema está desarrollado sobre ASP.NET Core Blazor (Web App, render interactivo en servidor) con EF Core sobre SQL Server, autenticación por cookie con roles y despliegue dual: IIS o Windows Service."
P "Estado actual: funcionalidad completa e instalable. El instalador compila y la release v$Version está publicada en GitHub con el EXE del instalador y el ZIP de publicación. Queda pendiente conectar la instancia real de SQL Server y configurar el origen de datos operacional." -Italic

H2 "1.1 Funcionalidades entregadas"
Bullet "Inicio (Home) con estado en línea de VTI/VTech, última actualización, distribución por tipo, equipos por área, últimos equipos y accesos rápidos."
Bullet "Autenticación por cookie con roles Administrador y Usuario; menú y rutas protegidas."
Bullet "Configuración en 6 pestañas: SQL Server (configuración + operacional), MAPICS (ODBC), tablas del sistema, equipos, tablas VTI/VTech e Import/Export JSON."
Bullet "Persistencia de las conexiones en la base de configuración, cifradas con DPAPI."
Bullet "Dos orígenes de datos: base de configuración (catálogo de la app) y base operacional (tablas VTI/VTech) que consulta /query."
Bullet "Consulta dinámica con selección de tabla, columnas, filtros por tipo, área, equipo (DeviceId) y rango de fechas, más gráfico interactivo."
Bullet "Exportación de resultados a Excel y a PDF."
Bullet "Usuarios: alta, edición, borrado y activación con hash de contraseña."
Bullet "Sistema de actualizaciones: consulta a GitHub Releases desde el servidor y aviso al administrador y al resto de usuarios."
Bullet "Instalador Inno Setup dual (IIS o Windows Service) y publicación automatizada en GitHub Actions."
Bullet "Health checks en /health (JSON) y /health/live (texto)."

H2 "1.2 Pila tecnológica"
Table @("Capa", "Tecnología") @(
  @("Front-end", "ASP.NET Core Blazor Web App (render interactivo en servidor), Bootstrap 5, Chart.js 4"),
  @("Back-end", ".NET 10 (ASP.NET Core), Serilog, Health Checks"),
  @("Datos", "EF Core 10 + SQL Server (Microsoft.Data.SqlClient), ODBC para MAPICS"),
  @("Reportes", "ClosedXML (Excel), QuestPDF (PDF)"),
  @("Instalación", "Inno Setup 6, PowerShell, GitHub Actions (windows-latest)")
)

BreakPage

# ------------------------------------------------- 2. Arquitectura ---------
H1 "2. Arquitectura"
P "Solución con separación de responsabilidades en cinco proyectos:"
Code @(
  "SXA.RTX.Analytics.Domain          Entidades, enums, value objects (sin dependencias).",
  "SXA.RTX.Analytics.Application     Casos de uso, DTOs, interfaces de reporting.",
  "SXA.RTX.Analytics.Infrastructure  EF Core, SQL Server, ODBC, exportadores Excel/PDF.",
  "SXA.RTX.Analytics.Reporting       Reporting Engine y abstraccion IDataSourceProvider.",
  "SXA.RTX.Analytics.Web             UI Blazor, autenticacion, endpoints API, hosting."
)
P "Flujo de consulta:"
Code @(
  "DataSource  ->  Query  ->  QueryResult  ->  Report  ->  Visualization  ->  Dashboard",
  "",
  "IDataSourceProvider.ExecuteAsync abstrae las diferencias entre SQL Server y ODBC,",
  "de modo que el engine se puede usar desde Blazor, API, jobs y exportadores."
)

H2 "2.1 Modelo de datos"
Table @("Objeto", "Descripción") @(
  @("SXA_RTX_ApplicationSettings", "Clave/valor de configuración de la aplicación"),
  @("SXA_RTX_AuditLogs", "Bitácora de operaciones y accesos"),
  @("SXA_RTX_TablasConfig", "Catálogo de tablas operativas y su tipo VTI/VTech"),
  @("SXA_RTX_Equipos", "Equipos con DeviceId, nombre y área"),
  @("Users", "Usuarios de la plataforma (nombre, hash, rol, activo)"),
  @("dbo.SXA_PCs", "Catálogo de origen: NombrePC, TipoMaquina, UltimoContacto")
)
P "El DeviceId tiene el formato PC-<16 hex> y lo genera SXA-RTX-Sync. La plataforma resuelve el nombre y el área del equipo consultando dbo.SXA_PCs."

BreakPage

# ------------------------------------------------- 3. Seguridad ------------
H1 "3. Seguridad y control de acceso"
Bullet "Autenticación por cookie propia (esquema SXA_RTX_Auth), sin dependencia de Identity."
Bullet "Rutas protegidas: Inicio, Configuración, Usuarios y Consulta."
Bullet "Roles: Administrador (todo) y Usuario (solo Inicio y Consulta)."
Bullet "Contraseñas almacenadas como hash; nunca se registran en logs."
Bullet "El servidor de datos de reporting debe usarse con una cuenta de solo lectura."
Bullet "Las cadenas de conexión nunca se versionan: se configuran en /configuration o con la variable de entorno ConnectionStrings__ConfigurationDatabase en el App Pool."
Bullet "Usuario inicial de fábrica: ECCSA. Debe cambiarse en la primera instalación."

H2 "3.1 Pantalla de acceso"
P "Acceso a pantalla completa. Sin sesión iniciada, cualquier ruta protegida redirige aquí y el menú no se renderiza."
Shot "01-login.png" "Figura 1. Inicio de sesión a pantalla completa. Las credenciales de fábrica no se muestran en pantalla."

BreakPage

# ------------------------------------------------- 4. Módulos --------------
H1 "4. Módulos funcionales"

H2 "4.1 Inicio"
P "Tablero de monitoreo con estado de línea de VTI y VTech, última actualización, distribución por tipo, equipos por área, últimos equipos y accesos rápidos."
Shot "02-home.png" "Figura 2. Inicio: KPIs de VTI/VTech, distribución por tipo y accesos rápidos."
Shot "03-home-completo.png" "Figura 3. Inicio completo (captura de página completa)."

H2 "4.2 Configuración"
P "Seis pestañas. La primera define las dos conexiones SQL (configuración y operacional) y permite guardarlas; la de MAPICS registra la conexión ODBC."
Shot "04-configuracion-sql.png" "Figura 4. Arriba, base de configuración con el botón Guardar conexiones. Abajo, base operacional (VTI/VTech) que consulta /query. Las contraseñas se cifran con DPAPI y nunca se muestran."
Shot "05-configuracion-mapics.png" "Figura 5. Configuración - conexión MAPICS por ODBC."
Shot "06-configuracion-tablas-sistema.png" "Figura 6. Configuración - tablas del sistema SXA_RTX_*."
Shot "07-configuracion-equipos.png" "Figura 7. Configuración - equipos: DeviceId, tipo (de SXA_PCs), nombre y área."
Shot "08-configuracion-tablas-vti-vtech.png" "Figura 8. Configuración - tablas operativas VTI/VTech."
Shot "09-configuracion-importexport.png" "Figura 9. Import/Export en JSON. El export nunca incluye cadenas de conexión ni hashes de contraseña; los usuarios importados quedan inactivos y con contraseña pendiente."

H2 "4.3 Consulta dinámica"
P "Se eligen tabla, columnas y filtros. El listado de tablas se filtra por el catálogo SXA_RTX_TablasConfig según el tipo VTI/VTech. La cabecera indica si los datos salen de la base operacional configurada o de la base de configuración. El resultado se puede graficar y exportar."
Shot "10-consulta-configurada.png" "Figura 10. Consulta dinámica con tabla y columnas seleccionadas."
Shot "11-consulta-resultados.png" "Figura 11. Resultado de la consulta con gráfico y tabla de datos (exportable a Excel y PDF)."

H2 "4.4 Usuarios"
Shot "12-usuarios.png" "Figura 12. Administración de usuarios: alta, edición, borrado y activación."

BreakPage

# ------------------------------------------ 5. Sistema de actualización -----
H1 "5. Sistema de actualizaciones"

H2 "5.1 Cómo funciona"
P "El aviso de actualización se alimenta de GitHub Releases; no hay un servidor de versiones propio. Solo el servidor consulta GitHub, así que el coste no depende del número de usuarios."
Code @(
  "1. UpdateCheckWorker (hosted service) consulta, cada 60 min por defecto:",
  "     GET https://api.github.com/repos/hector1516/SXA-RTX-Analytics/releases/latest",
  "2. Compara la version ensamblada (p. ej. 1.1.3.0) con el tag publicado (v1.1.3)",
  "   usando Version, no comparacion de cadenas.",
  "3. Guarda el resultado en memoria. /api/updates/check responde desde esa cache,",
  "   de modo que cargar una pagina no genera trafico a GitHub.",
  "4. El navegador consulta /api/updates/check al cargar y cada 60 min:",
  "     Administrador   -> modal 'Actualizacion disponible' + changelog",
  "     Resto usuarios  -> modal 'Novedades' una vez (localStorage)",
  "5. Boton Actualizar / Ver release -> abre la pagina del release en GitHub."
)
P "Si GitHub no responde se conserva el ultimo resultado correcto y se registra el error en el log; la app sigue funcionando con normalidad."

H2 "5.2 Qué hace y qué no hace"
Table @("Comportamiento", "Estado") @(
  @("Detecta que existe una versión nueva", "Implementado"),
  @("Muestra changelog al administrador", "Implementado"),
  @("Muestra novedades al resto de usuarios una vez", "Implementado"),
  @("Descarga e instala la actualización automáticamente", "No implementado"),
  @("Reinicia la aplicación tras instalar", "No implementado")
)
P "Importante: el botón Actualizar abre GitHub Releases. La actualización real se hace ejecutando el instalador publicado en el servidor y reiniciando el App Pool de IIS (o el servicio de Windows)."

H2 "5.3 Configuración del intervalo"
Bullet "UpdateCheck:IntervalMinutes en appsettings.json (60 por defecto)."
Bullet "Clave App.UpdateCheckMinutes para ajustarlo en caliente; 0 desactiva la comprobación."

H2 "5.4 Captura del aviso"
Shot "15-actualizacion-disponible.png" "Figura 13. Aviso de actualizacion disponible para el administrador (escenario simulado: app en una version anterior y release nuevo publicado en GitHub)."

BreakPage

# ------------------------------------------ 6. Despliegue --------------------
H1 "6. Despliegue e instalación"

H2 "6.1 Artefactos publicados (v$Version)"
Table @("Artefacto", "Tamaño", "Uso") @(
  @("Setup_SXA_RTX_Analytics_v$Version.exe", "~50 MB", "Instalador con asistente (detecta IIS o instala como Windows Service)"),
  @("SXA-RTX-Analytics-v$Version-publish.zip", "~70 MB", "Publicación para copiar manualmente a un sitio IIS existente")
)

H2 "6.2 Requisitos del servidor"
Bullet "Windows Server 2019 o superior (o Windows 10/11 con IIS)."
Bullet "ASP.NET Core Hosting Bundle 10.0 (instalador oficial de Microsoft)."
Bullet "SQL Server con la base de configuración SXA_RTX_Analytics."
Bullet "Permisos de administrador para el instalador (crea App Pool, sitio y regla de firewall)."

H2 "6.3 Instalación en IIS (flujo del instalador)"
Code @(
  "1. Copia la publicacion a  C:\Program Files\SXA-RTX Analytics",
  "2. Comprueba el Hosting Bundle (avisa si falta).",
  "3. Crea el App Pool 'SXA-RTX-Analytics' (ApplicationPoolIdentity, sin CLR gestionado).",
  "4. Crea el sitio 'SXA-RTX-Analytics' en http://localhost:5000.",
  "5. Ejecuta iisreset.",
  "6. Configurar despues ConnectionStrings__ConfigurationDatabase en el App Pool."
)
H2 "6.4 Alternativa como Windows Service"
P "Si no se detecta IIS, el instalador registra el servicio SXA-RTX-Analytics con arranque automático, reinicio ante fallo y regla de firewall en el puerto 5000."

H2 "6.5 Publicación automatizada"
P "El workflow .github/workflows/release.yml se ejecuta en cada tag v*. Hace build y test de la solución, instala Inno Setup, publica la aplicación, compila el instalador, comprime la publicación y crea el release en GitHub."

BreakPage

# ------------------------------------------ 7. Salud y observabilidad ---------
H1 "7. Salud y observabilidad"
P "Health checks expuestos y enlazados de forma discreta desde el pie de página."
Code @(
  "/health        JSON con el detalle de cada check (nombre, estado, duracion)",
  "/health/live   Texto plano: Healthy | Unhealthy"
)
P "Logging estructurado con Serilog en consola y en el archivo logs/sxa-rtx-analytics-AAAAMMDD.log. No se registran contraseñas, tokens ni cadenas de conexión."

BreakPage

# ------------------------------------------ 8. Pruebas ----------------------
H1 "8. Pruebas y calidad"
Table @("Proyecto", "Pruebas", "Resultado") @(
  @("SXA.RTX.Analytics.Domain.Tests", "4", "Correctas"),
  @("SXA.RTX.Analytics.Application.Tests", "2", "Correctas"),
  @("SXA.RTX.Analytics.Reporting.Tests", "3", "Correctas"),
  @("SXA.RTX.Analytics.Infrastructure.Tests", "21", "Correctas"),
  @("Total", "30", "30 correctas, 0 fallidas")
)
P "Los tests de Infrastructure cubren la precedencia de las conexiones (valor guardado > appsettings > variable de entorno), el cifrado de secretos, que el export JSON nunca filtra contraseñas ni hashes, que el import deja los usuarios inactivos y bloqueados, y los timeouts de conexión SQL."
P "La compilación de la solución en Release finaliza con 0 errores."

BreakPage

# ------------------------------------------ 9. Pendientes -------------------
H1 "9. Limitaciones y trabajo pendiente"
H2 "9.1 Pendiente de validar en planta"
Bullet "Conectar el SQL Server real: hasta ahora la app opera con EF Core InMemory y datos de demostracion. Hay que crear la base SXA_RTX_Analytics en el servidor y cargar las tablas."
Bullet "Configurar la base operacional en /configuration y comprobar que la cabecera de /query deja de mostrar 'Base de configuracion'."
Bullet "Ajustar los timeouts (5 s de conexion, 15 s de comando) si la red de planta lo exige."
Bullet "Como el esquema se crea con EnsureCreated, una base existente no gana columnas nuevas (por ejemplo Equipo.Tipo). Para produccion conviene migrar a migraciones de EF Core."

H2 "9.2 Pendiente - instalador y despliegue"
Bullet "El instalador detecta IIS pero no instala el Hosting Bundle; debe descargarse e instalarse previamente."
Bullet "El instalador no solicita la cadena de conexión ni crea la base de datos."
Bullet "El sitio se crea en el puerto 5000; conviene parametrizarlo y configurar HTTPS."

H2 "9.3 Pendiente - actualizaciones automáticas"
Bullet "Implementar descarga e instalación automática y reinicio del App Pool si se requiere actualización desatendida."

H2 "9.4 Otros puntos técnicos"
Bullet "La exportación a PDF incluye la tabla y el resumen, pero no la imagen del gráfico."
Bullet "El proyecto apunta a net10.0; la especificación original era .NET 8 LTS. Verificar la versión del runtime en el servidor."
Bullet "Las capturas de esta informe se generaron en un navegador sin tipografía de emoji; en el equipo del usuario los iconos se muestran correctamente."

BreakPage

# ------------------------------------------ 10. Anexo -----------------------
H1 "10. Anexo - Endpoints y artefactos"
H2 "10.1 Endpoints"
Table @("Endpoint", "Método", "Autenticación", "Descripción") @(
  @("/api/auth/login", "POST", "No", "Inicia sesión y emite la cookie"),
  @("/api/version", "GET", "No", "Versión ensamblada en ejecución"),
  @("/api/updates/check", "GET", "No", "Compara con el último release de GitHub"),
  @("/health", "GET", "No", "Estado detallado en JSON"),
  @("/health/live", "GET", "No", "Estado en texto plano"),
  @("/logout", "GET", "No", "Cierra la sesión y vuelve al login")
)
H2 "10.2 Archivos de referencia"
Code @(
  "publish-analytics.ps1                 Publicacion + compilacion del instalador",
  "installer/Analytics.iss               Definicion del instalador Inno Setup",
  "installer/install.ps1                  Configuracion de IIS o del servicio Windows",
  ".github/workflows/release.yml          Publicacion automatizada en GitHub",
  "docs/DEPLOYMENT.md                     Guia de despliegue en IIS",
  "docs/ARCHITECTURE.md                   Arquitectura por capas",
  "docs/DATABASE.md                       Modelo de datos",
  "docs/SECURITY.md                       Politicas de seguridad",
  "docs/REPORTING.md                      Motor de reporting",
  "docs/report/screens/                   Capturas de esta informe"
)
H2 "10.3 Reutilizar las capturas"
P "Las capturas se generan con Edge + puppeteer-core en una sesión autenticada única (login por API) para evitar redirecciones al login. Script de referencia: docs/report/build-report.ps1 documenta el contenido; la captura se puede reproducir con node y puppeteer-core apuntando al Edge instalado."

$sel.ParagraphFormat.Alignment = 0

# ------------------------------------------------- pie de pagina -----------
$footer = $doc.Sections.Item(1).Footers.Item(1).Range
$footer.Text = "SXA-RTX Analytics - Informe v$Version - ECCSA Automation - Uso interno          Pagina "
$footer.Font.Size = 8
$footer.ParagraphFormat.Alignment = 1
$footer.Collapse(0) | Out-Null
$footer.Fields.Add($footer, 33) | Out-Null   # wdFieldPage

$header = $doc.Sections.Item(1).Headers.Item(1).Range
$header.Text = "SXA-RTX Analytics | Informe de avance funcional y despliegue"
$header.Font.Size = 8
$header.Font.Italic = 1
$header.ParagraphFormat.Alignment = 2

# ------------------------------------------------------------ guardar -------
Write-Host "Guardando DOCX..."
$docx = "$Base.docx"
$doc.SaveAs([ref]$docx, [ref]16)      # wdFormatDocumentDefault
Write-Host "Guardando ODT..."
$odt = "$Base.odt"
$doc.SaveAs([ref]$odt, [ref]23)        # wdFormatOpenDocumentText
Write-Host "Exportando PDF..."
$pdf = "$Base.pdf"
$doc.ExportAsFixedFormat($pdf, 17)     # wdExportFormatPDF

$pages = $doc.ComputeStatistics(2)      # wdStatisticPages
$doc.Close(0)
$word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
[GC]::Collect()

Write-Host ""
Write-Host "Documentos generados:"
foreach ($f in @($docx, $odt, $pdf)) {
  if (Test-Path $f) {
    $kb = [math]::Round((Get-Item $f).Length / 1KB, 1)
    Write-Host ("  {0}  ({1} KB)" -f (Split-Path $f -Leaf), $kb)
  } else {
    Write-Host ("  FALLO: {0}" -f $f)
  }
}
Write-Host "Paginas: $pages"
