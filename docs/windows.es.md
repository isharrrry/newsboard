# Hosts de Windows

[English](windows.md) | [中文](windows.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

Esta página es **una de las páginas de plataforma**; para la visión general multiplataforma, véanse el [README](../README.es.md) y [Arquitectura](architecture.es.md), y el lado de Android en [Android](android.es.md).

Windows tiene **dos** hosts. Comparten los mismos componentes Razor (`NewsBoard.Shared`, cero dependencias de plataforma), así que la interfaz y la configuración son idénticas: solo cambia la profundidad con que se implementan las capacidades de plataforma.

## Los dos hosts

| Host | Tecnología / TFM | Notas |
|---|---|---|
| `src/NewsBoard.Wpf` | WPF + WebView2, `net10.0-windows10.0.19041.0` | Host de escritorio; admite pantalla completa sin bordes y su protección de apagado es «primer plano o pantalla completa» |
| la cabecera Windows de `src/NewsBoard.Maui` | .NET MAUI + `BlazorWebView`, el mismo TFM | Comparte el código de host con la cabecera de Android; no hace pantalla completa sin bordes, así que su protección se degrada a «solo primer plano» |

| Capacidad | `NewsBoard.Wpf` | Cabecera MAUI de Windows |
|---|---|---|
| Pantalla completa sin bordes | admitida (F11 entra, Esc sale) | no implementada (`SetFullscreenAsync` no hace nada) |
| Protección del apagado | «la ventana de la aplicación está en primer plano **o** en pantalla completa sin bordes» | «solo primer plano» (la condición de pantalla completa siempre es falsa) |
| Mantener la pantalla encendida | sin efecto | sin efecto |

## Requisitos

| Pieza | Requisito |
|---|---|
| .NET SDK | 10 (en esta máquina `10.0.203`, runtime `10.0.7`) |
| WebView2 Runtime | lo necesitan los dos hosts; medido `154.0.4258.62` aquí |
| Windows App Runtime | la cabecera MAUI de Windows se ejecuta **sin empaquetar** (`WindowsPackageType=None`) y lo necesita; aquí están 1.6 / 1.7 / 1.8 |
| workload | `maui-windows` (instalado aquí) |

El host de WPF no necesita el Windows App Runtime; sin él, la cabecera MAUI de Windows no arranca.

## Compilación y ejecución

```powershell
# Host de WPF (también la forma más rápida de depurar la capa Blazor)
dotnet run --project src\NewsBoard.Wpf

# Cabecera MAUI de Windows: primero compilar, luego ejecutar
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# Publicación de WPF (Release)
dotnet publish src\NewsBoard.Wpf -c Release
```

`dotnet publish -c Release` de WPF produjo **45 archivos / 28.14 MB** y se ejecutó directamente desde el directorio de publicación (título de ventana «新闻看板», 6 procesos hijos `msedgewebview2`). Esa salida no es autocontenida ni de archivo único.

## Apagado: el monitor se apaga de verdad

Cuando el lado de Windows entra en apagado, difunde `WM_SYSCOMMAND` / `SC_MONITORPOWER` (`2` = apagar, `-1` = encender):

```
SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)
```

La decisión y la idempotencia viven en la lógica pura compartida `MonitorPowerPolicy`: el monitor se apaga **solo cuando «la ventana de la aplicación está en primer plano o en pantalla completa sin bordes»** (para no dejar a oscuras a un usuario que trabaja en otro programa), pero **volver a encenderlo no está sujeto a esa protección** (si no, tras apagar la pantalla y cambiar de aplicación el usuario nunca podría despertarla). La cabecera MAUI de Windows no hace pantalla completa sin bordes, así que en ese lado la condición es siempre `false` y la protección se degrada a «solo primer plano».

A diferencia de Android, aquí **el monitor se apaga de verdad**, no se pone el brillo de la ventana a 0.

## Mantener la pantalla encendida

En los dos hosts de Windows, `IScreenController.SetKeepScreenOnAsync` es un **método sin efecto** — un escritorio no lo necesita.

## Pantalla completa

El host de WPF admite pantalla completa sin bordes; medido: la ventana por defecto `1600x1000@(160,10)` → tras F11 `1920x1080@(0,0)` ocupando toda la pantalla → Esc la restaura, y el resultado se guarda en la configuración.

La cabecera MAUI de Windows **no** hace pantalla completa sin bordes (`SetFullscreenAsync` no hace nada).

## Voz

Los dos hosts de Windows usan `System.Speech` (`SpeechSynthesizer`). Medido:

- Se enumera la voz china `Microsoft Huihui Desktop` (zh-CN).
- La devolución de llamada `SpeakCompleted` es fiable: se disparó exactamente una vez tras una locución de **6135 ms**, produciendo un WAV de 290 KB.
- Cancelar (`SpeakAsyncCancelAll`) devolvió en **744 ms**.

La velocidad y el tono se mapean desde la configuración — el tono pasa por SSML y, si falla, se degrada a texto plano.

## Inicio automático al arrancar

Se escribe en la clave del registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. El ciclo Enable → `IsEnabled` → Disable pasó la prueba y tras Disable no queda ningún valor.

## Configuración, caché y clave de API

El host de WPF y la cabecera MAUI de Windows **comparten exactamente los mismos archivos**:

| Contenido | Ruta |
|---|---|
| Configuración | `%LOCALAPPDATA%\NewsBoard\config.json` |
| Caché de noticias | `%LOCALAPPDATA%\NewsBoard\news.json` |
| Clave de API | `%APPDATA%\NewsBoard\secret.dat` (DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`, **nunca en texto plano**) |

Los dos hosts comparten un único `secret.dat`, así que la clave sirve en cualquiera; cada sobrescritura de `config.json` deja un `config.json.bak`. La variable de entorno `NEWSBOARD_DATA_DIR` puede sobrescribir el directorio de datos (común a los tres hosts).

## Trampas conocidas

1. **WPF debe usar el TFM con versión.** Un `net10.0-windows` pelado muere al arrancar con `FileNotFoundException: Microsoft.Windows.SDK.NET` — `BlazorWebView` usa internamente `WebView2CompositionControl` (composición D3D) y necesita los ensamblados de proyección de CsWinRT, que solo aporta el TFM con versión.
2. **Nunca ejecutes `Stop-Process -Name msedgewebview2` de forma global.** También mata el WebView2 de las instancias recién creadas y corrompe su directorio de datos de usuario, tras lo cual la cabecera MAUI de Windows arranca en una ventana negra y se cierra sola cada vez; borrar `bin\...\NewsBoard.Maui.exe.WebView2` lo restaura. Mata el proceso de la aplicación en sí.
3. **Antes de inyectar la rueda del ratón o pulsaciones en un host WebView2, haz clic primero en el área de contenido de la ventana de destino para darle el foco**, si no los eventos nunca llegan a WebView2 (medido: sin el clic la rueda no hace absolutamente nada; con él `scrollTop` crece desde 0 como se espera).

`AGENTS.md` sigue siendo la fuente autoritativa de todos los hechos medidos de Windows y de las trampas.

Relacionado: [Contribuir](contributing.es.md) · [Solución de problemas](troubleshooting.es.md) · [Android](android.es.md) · [Índice de documentación](index.es.md)
