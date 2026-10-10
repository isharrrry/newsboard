# Contribuir

[English](contributing.md) | [中文](contributing.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

Esta página es la versión corta de `AGENTS.md`: qué instalar, qué ejecutar, qué no se puede cambiar y qué sigue sin verificarse. `AGENTS.md` sigue siendo la fuente autoritativa de todos los hechos medidos y las trampas encontradas.

---

## 1. Entorno

Medido en esta máquina:

| Elemento | Valor medido |
|---|---|
| .NET SDK | 10.0.203 (runtime 10.0.7) |
| Workloads | `maui-android` 10.0.20 / 10.0.100 y `maui-windows` — ambos instalados |
| Windows App Runtime | 1.6 / 1.7 / 1.8 (el host MAUI de Windows se ejecuta sin empaquetar, `WindowsPackageType=None`) |
| WebView2 Runtime | 154.x (los dos hosts de Windows) |
| JDK | 17 en `D:\Android\jdk-17` (Microsoft OpenJDK 17.0.20.1 LTS) |
| Android SDK | `D:\Android\Sdk` — platform-36 / build-tools 36.0.0 / platform-tools |
| Android NDK | 27.2.12479018 |

Windows necesita el WebView2 Runtime. Las compilaciones de Android necesitan la cadena de herramientas de arriba y, en cada shell nuevo, el script de entorno.

---

## 2. Comandos de compilación y pruebas

```powershell
# Biblioteca compartida (aquí vive toda la lógica pura y la UI)
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo

# Pruebas unitarias
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo

# Host de WPF (escritorio de Windows; también la forma más rápida de depurar la capa Blazor)
dotnet run --project src\NewsBoard.Wpf

# Cabecera MAUI de Windows (sin empaquetar)
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# APK de Android (salida: bin\Debug\net10.0-android\*-Signed.apk)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Android: compilar + instalar en el dispositivo conectado — los paquetes Debug SOLO se pueden implementar así
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo

# Solución completa (WPF + MAUI/Android + MAUI/Windows)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo
```

Cargar `scripts\android-env.ps1` es obligatorio antes de cualquier compilación de Android: sin él aparece `XA5300 找不到 Android SDK`. El script es idempotente y solo define variables en el proceso actual — nunca toca variables de entorno de máquina ni de usuario. La política de ejecución local es `Restricted`, de ahí la línea `Set-ExecutionPolicy -Scope Process`.

---

## 3. Estructura del repositorio y propiedad de archivos

| Área | Archivos | Responsable |
|---|---|---|
| Contrato congelado (**nadie puede cambiarlo**) | `Models/NewsItem.cs`, `Services/AppConfig.cs`, `Services/Abstractions/*.cs`, `_Imports.razor`, `Components/Routes.razor` | lead agent |
| Obtención + resumen con IA | `Services/RssNewsSource.cs`, `Services/WebSearchNewsSource.cs`, `Services/NewsSummarizer.cs`, `Services/NewsRepository.cs`, `Services/ImageResolver.cs`, `Services/NewsFetchCoordinator.cs` | SA1 |
| Configuración + persistencia | `Services/ConfigStore.cs`, `Components/Pages/Settings.razor`, ayudantes de la página de Ajustes distintos de `wwwroot/js/measure.js` | SA2 |
| Motor de reproducción + UI de reproducción | `Services/PlaybackEngine.cs`, `Components/Pages/Display.razor`, `Components/Shared/NewsCard.razor`, `wwwroot/css/app.css`, `wwwroot/js/measure.js` | SA3 |
| Host de WPF | `src/NewsBoard.Wpf/**` | SA4 |
| Host de Android + entorno | `src/NewsBoard.Maui/**` | SA5 |
| Pruebas | `src/NewsBoard.Tests/**` (un archivo por tema — no sobrescribas los archivos de otros) | cada agente |

---

## 4. Contratos congelados

No cambies estas firmas ni nombres de campo:

- `NewsBoard.Shared.Models.NewsItem` / `RawNewsItem` — incluidos `NewsItem.Importance` (`double?`, 0–1, null = sin puntuar) y `NewsItem.RawContent` (el contenido bruto obtenido), más `RawNewsItem.Importance` / `RawNewsItem.Description`.
- `NewsBoard.Shared.Services.AppConfig` y todos sus enumerados — incluidos `SummaryLanguage` (`SummaryLanguageOption`: `SimplifiedChinese` por defecto / `TraditionalChinese` / `English`, con `SummaryLanguageText.PromptRule()` para la restricción del prompt y `DisplayName()` para el texto de la interfaz), `ClockFontSizeSp` (por defecto 32), `ClockFontFamily` (por defecto `""`), `ImportanceThreshold` (por defecto 0.7), `RetentionDays` (por defecto 3). `PlayOrder` es **Random** por defecto.
- `NewsBoard.Shared.Services.Abstractions`: `IScreenController`, `ISpeechService`, `ISecretStore`, `IAutoStart`, `ILinkOpener`, `INewsSource`, `IConfigProvider`, `INewsRepository`, `IClock` (con `SystemClock`).
- La API de control manual de `PlaybackEngine`: `NextAsync()` / `PreviousAsync()` / `InterruptAsync()` (interrumpe esta ronda → apagado durante los segundos de "duración del apagado" → siguiente ronda; se apaga una vez incluso con `BlackoutCycleEnabled=false`), con los cambios de dirección dando la vuelta a la lista; `WakeAsync()` (durante el apagado, un toque en la pantalla vuelve a mostrar y reinicia la ventana desde el elemento actual; durante la visualización no tiene efecto secundario, ni avanza ni reinicia la ventana).
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` / `InitializeNewsBoardAsync()` — el único punto de ensamblaje para los hosts (registra primero las implementaciones de plataforma y luego llámalo; el arranque debe esperar `InitializeNewsBoardAsync()` o la lista de reproducción queda vacía).
- El conjunto de `using` de `_Imports.razor`, y `Components/Routes.razor` como `BlazorWebView.RootComponent`.

Para añadir una interfaz o un campo, usa `partial` en tu propio archivo o mantenlo privado dentro de la implementación — no toques los archivos de arriba.

---

## 5. Convenciones de código

- Frameworks de destino: Shared/pruebas = `net10.0`; **WPF = `net10.0-windows10.0.19041.0` (el número de plataforma con versión es obligatorio)**; MAUI = `net10.0-android` + `net10.0-windows10.0.19041.0`. Un `net10.0-windows` pelado hace que el host de WPF muera al arrancar con `FileNotFoundException: Microsoft.Windows.SDK.NET`, porque `BlazorWebView` usa internamente `WebView2CompositionControl` (composición D3D) y necesita los ensamblados de proyección de CsWinRT que solo aporta el TFM con versión.
- **Sin dependencias NuGet de terceros** — HTTP usa `HttpClient`, JSON usa `System.Text.Json`, RSS usa `XDocument`, las pruebas usan xunit. Las únicas excepciones son `Microsoft.AspNetCore.Components.WebView.Wpf` (10.0.0) del host de WPF, `System.Speech` (10.0.7, la implementación de TTS que indica el plan) y `System.Security.Cryptography.ProtectedData` (10.0.0), que la cabecera MAUI de Windows necesita porque MAUI no usa el framework WindowsDesktop. En el lado de WPF, DPAPI **no** necesita paquete — `UseWPF=true` lo aporta, y añadir el paquete provoca `NU1510`.
- `Nullable` e `ImplicitUsings` están activados; los comentarios del código se escriben en chino simplificado y solo donde realmente hacen falta.
- El proyecto Shared tiene **cero dependencias de plataforma** — nada de `#if ANDROID` / `#if WINDOWS`. Las capacidades de plataforma se inyectan a través de las interfaces de `Abstractions`.
- Todo texto nuevo visible para el usuario se escribe en chino simplificado.
- `dotnet build` debe pasar antes de hacer commit, y cualquier rama con lógica necesita una prueba de xunit.

---

## 6. Puerta previa al commit

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo     # debe ser 0 advertencias y 0 errores en las tres cabeceras
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

- La compilación de toda la solución debe quedar en **0 advertencias / 0 errores** (WPF + MAUI/Android + MAUI/Windows).
- `dotnet test` debe estar totalmente en verde. Línea base medida: **224/224**.
- No entregues un cambio con una prueba roja conocida. Si una prueba es realmente inestable, corrige primero la inestabilidad — en la entrada 10 de Solución de problemas se explica cómo se estabilizaron las pruebas sensibles al tiempo.

---

## 7. Disciplina de documentación

Un cambio de comportamiento, de un elemento de configuración o de un comando debe actualizar la documentación **en el mismo commit**:

| Qué cambió | Qué tocar |
|---|---|
| Comportamiento visible / cómo ejecutar | los tres archivos `README.*` |
| Una página temática | las tres versiones de idioma de la página `docs/` afectada (sobre todo `configuration` cuando cambia un elemento de configuración) |
| Convenciones o hechos medidos | `AGENTS.md` |
| El plan, la lista de configuración, el historial | `plans/news-board.plan.md` (§4 lista de configuración + la sección de iteración correspondiente + §10 registro de revisiones) |
| Cualquier cosa que note un usuario | `CHANGELOG.md` |

**Ningún elemento de configuración puede existir en el código y no en la documentación.** Del mismo modo, los números de la documentación deben ser valores medidos que existan en este repositorio — si una afirmación no tiene una medición detrás, escribe "sin verificar" o elimínala.

---

## 8. Aún sin verificar

Tal cual, no como "debería funcionar":

1. **El apagado real del monitor en Windows no se ha confirmado a mano.** `SC_MONITORPOWER` y la protección `MonitorPowerPolicy` están implementados, pero ninguna ejecución automática ha observado un cambio real de retroiluminación.
2. **El inicio automático al arrancar en Android nunca ha funcionado ni una vez.** La cadena es correcta, pero MIUI lo bloquea hasta que el usuario concede el permiso de inicio automático a mano; antes de eso el registro del sistema termina en `process is not permitted to auto start`.
3. **La estabilidad de Android solo está medida durante 20 minutos** (sin fallos, sin ANR, memoria estabilizada entre 295 y 302 MB), mientras que el criterio de aceptación del plan es de 2 horas.
4. **La pantalla completa inmersiva en Android (barra de estado oculta) no se ha verificado** en un dispositivo.
5. **Nunca se ha instalado un APK de Release en un dispositivo** — solo se implementaron compilaciones Debug (Debug 14.97 MB / Release 28.77 MB).
6. **El trimming / AOT de Release no está activado**, y la salida de publicación de WPF no es autocontenida ni de archivo único.
7. **Cambiar el idioma del resumen no reescribe los resúmenes en caché** — hay que borrar `news.json` o esperar a que venza la retención por día natural.
8. **El filtro "mostrar solo los elementos que coinciden con una palabra clave" no existe**; por ahora una palabra clave es solo una etiqueta de agrupación.
9. **La velocidad de desplazamiento guiada por TTS** ("desplazarse hasta el final justo cuando termina la lectura") solo está cubierta por pruebas unitarias, sin verificación a nivel de audio.
10. **El ciclo "cambiar ajustes en la interfaz → matar el proceso → reiniciar"** no está automatizado.
11. **No hay emulador / AVD** en esta máquina.

---

Relacionado: [Solución de problemas](troubleshooting.es.md) · [Arquitectura](architecture.es.md) · [Notas de Android](android.es.md) · [Índice de documentación](index.es.md)
