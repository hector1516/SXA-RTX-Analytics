# Informe de SXA-RTX Analytics

Esta carpeta contiene el informe funcional y de despliegue de la plataforma,
con capturas reales de la aplicación.

## Archivo generado

| Archivo | Descripción |
| --- | --- |
| `SXA-RTX-Analytics-Informe-v<VERSION>.docx` | Informe en Word, editable (20 páginas) |
| `build-report.ps1` | Script que lo genera |
| `screens/` | Capturas usadas en el informe |

Se entrega **solo en Word**. Si alguna vez hace falta OpenDocument o PDF, el
mismo script puede añadirlos sin tocar nada más:

```powershell
$doc.SaveAs([ref]"$Base.odt", [ref]23)             # OpenDocument
$doc.ExportAsFixedFormat("$Base.pdf", 17)          # PDF
```

## Regenerar el informe

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs\report\build-report.ps1 -Version 1.1.3
```

Requiere Microsoft Word instalado (se usa como motor de composición). El script
arma el documento a partir de las imágenes de `screens/`, así que solo hay que
reemplazar las capturas para refrescar el contenido.

## Regenerar las capturas

Las capturas se obtienen con Edge + `puppeteer-core`. Es importante hacer el
login **por API dentro del mismo contexto del navegador**, no escribiendo la
contraseña con teclado: así todas las páginas protegidas se capturan en una
sola sesión y ninguna redirige a `/login`.

```powershell
# 1. Levantar la app
dotnet publish src\SXA.RTX.Analytics.Web\SXA.RTX.Analytics.Web.csproj -c Release -o artifacts\publish /p:Version=1.1.2 /p:VersionPrefix=1.1.2
artifacts\publish\SXA.RTX.Analytics.Web.exe --urls http://127.0.0.1:5149

# 2. Capturar (node capture.js)
#    - 01            login (sin sesión)
#    - POST /api/auth/login  -> cookie SXA_RTX_Auth
#    - 02..12        páginas protegidas en la misma sesión
#    - 13, 14        /health y /health/live
```

Para la captura del aviso de actualización hay que publicar la app con una
versión anterior a la del release (por ejemplo `1.1.0` frente a un release
`1.1.2`) y volver a publicar después con la versión correcta.

## Nota

Las capturas se generaron en un navegador sin tipografía de emoji, por lo que
algunos iconos de la interfaz aparecen como `?`. En el equipo del usuario se
muestran correctamente.