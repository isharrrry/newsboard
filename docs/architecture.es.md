# Arquitectura

[English](architecture.md) | [中文](architecture.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

## Los tres proyectos

| Proyecto | Framework de destino | Qué es |
|---|---|---|
| `NewsBoard.Shared` | `net10.0` | Biblioteca de clases Razor (RCL): **todos** los componentes de UI, `wwwroot` (css/js), el modelo de configuración, la obtención, el resumen con modelo, el motor de reproducción y las interfaces de abstracción de plataforma. **Cero dependencias de plataforma.** |
| `NewsBoard.Maui` | `net10.0-android`; `net10.0-windows10.0.19041.0` | Host de Android (MAUI Blazor Hybrid), y el proyecto que produce el APK. El framework de destino de Windows se conserva para poder depurar la misma capa Blazor en un PC. Aporta las implementaciones de plataforma de Android y de Windows. |
| `NewsBoard.Wpf` | `net10.0-windows10.0.19041.0` | Host de escritorio de Windows: WPF más `Microsoft.AspNetCore.Components.WebView.Wpf` (WebView2), alojando los mismos componentes Razor. Aporta las implementaciones de plataforma de Windows. |

La dirección de dependencias es única: `Maui → Shared`, `Wpf → Shared`. **Shared nunca depende de un host.**

`NewsBoard.Wpf` debe usar el framework de destino **con número de versión**. Un `net10.0-windows` sin versión arranca y falla de inmediato con `FileNotFoundException: Microsoft.Windows.SDK.NET`, porque `BlazorWebView` usa internamente `WebView2CompositionControl` (composición D3D) y necesita los ensamblados de proyección de CsWinRT, que solo aporta el TFM versionado.

Por qué está dividido así:

- **Las capacidades de plataforma viven por completo en los proyectos host.** El control de pantalla, la lectura en voz alta, el almacenamiento de secretos, el inicio automático al arrancar y la apertura de enlaces se inyectan mediante interfaces definidas en Shared; Shared no contiene ningún `#if ANDROID` ni `#if WINDOWS`.
- **Shared solo contiene lógica .NET pura** — obtención, resumen con modelo, deduplicación, caché, la máquina de estados de reproducción y todos los componentes Razor. Una sola copia sirve a las tres cabeceras y la capa Blazor se puede depurar rápido en un PC con el host de WPF.
- **Blazor solo renderiza.** Los componentes reciben un `NewsItem` más las opciones de presentación y producen HTML/CSS.

## Árbol de código

Todos los archivos siguientes existen realmente en el repositorio (se omiten los artefactos de compilación de `bin/` y `obj/`).

```
NewsBoard/
├── NewsBoard.slnx
├── README.md / README.zh-CN.md / README.es.md
├── CHANGELOG.md
├── LICENSE                          (MIT)
├── AGENTS.md                        (convenciones para colaboradores con IA)
├── plans/
│   └── news-board.plan.md           (plan de implementación e historial)
├── scripts/
│   └── android-env.ps1              (hay que cargarlo con dot-source antes de compilar para Android)
├── docs/                            (este directorio)
└── src/
    ├── NewsBoard.Shared/            (net10.0, biblioteca de clases Razor, cero dependencias de plataforma)
    │   ├── NewsBoard.Shared.csproj
    │   ├── _Imports.razor
    │   ├── Models/
    │   │   └── NewsItem.cs                      (NewsItem / RawNewsItem)
    │   ├── Components/
    │   │   ├── Routes.razor                     (componente raíz de BlazorWebView)
    │   │   ├── Pages/Display.razor              (página del panel — la única página permanente)
    │   │   ├── Pages/Settings.razor             (página de Ajustes)
    │   │   └── Shared/NewsCard.razor            (renderiza un elemento: texto, o texto + imagen)
    │   ├── Services/
    │   │   ├── AppConfig.cs                     (todos los campos de configuración + Clone/NormalizeKeywords)
    │   │   ├── ArticleContentFetcher.cs         (cuerpo del artículo: tiempo de espera de 5 s, limpieza, corte del bloque de recomendaciones)
    │   │   ├── ConfigStore.cs                   (persistencia JSON, .bak antes de sobrescribir, clave en el almacén seguro de la plataforma)
    │   │   ├── ImageResolver.cs                 (búsqueda de imagen en cuatro niveles, comprobación con Range, una imagen por elemento)
    │   │   ├── ImportanceScoring.cs             (análisis tolerante, equilibrio por rangos cuando todo el lote degenera)
    │   │   ├── KeywordClassifier.cs             (clasifica por palabra clave configurada, con el nombre del canal como respaldo)
    │   │   ├── MonitorPowerPolicy.cs            (guardas e idempotencia del apagado real del monitor en Windows)
    │   │   ├── NewsBoardDataDirectory.cs        (punto único de resolución del directorio de datos)
    │   │   ├── NewsFetchCoordinator.cs          (una ronda de obtención: deduplicación → resumen → cuerpo → imágenes)
    │   │   ├── NewsFetchScheduler.cs            (obtiene al arrancar y luego repite por intervalo)
    │   │   ├── NewsRepository.cs                (caché, deduplicación por URL, filtro por umbral, poda por día natural)
    │   │   ├── NewsSummarizer.cs                (resumen + importancia + palabra clave, análisis JSON estricto, reintento)
    │   │   ├── PlaybackEngine.cs                (máquina de estados de reproducción, WakeAsync, compuerta del primer fotograma)
    │   │   ├── RetentionWindow.cs               (ventana de retención por día natural, lógica pura)
    │   │   ├── RssNewsSource.cs                 (fuente RSS de canal + cadena de respaldo)
    │   │   ├── ServiceCollectionExtensions.cs   (AddNewsBoardCore / InitializeNewsBoardAsync)
    │   │   ├── SummaryText.cs                   (revalidación local del resumen: corte en 1,2×, en un final de frase)
    │   │   ├── WebSearchNewsSource.cs           (extremo compatible con Anthropic + herramienta web_search)
    │   │   └── Abstractions/
    │   │       ├── IAutoStart.cs
    │   │       ├── IConfigProvider.cs
    │   │       ├── ILinkOpener.cs
    │   │       ├── INewsRepository.cs
    │   │       ├── INewsSource.cs               (`IClock` y `SystemClock` también se definen aquí)
    │   │       ├── IScreenController.cs
    │   │       ├── ISecretStore.cs
    │   │       └── ISpeechService.cs
    │   └── wwwroot/
    │       ├── css/app.css                      (tipografía grande, animación de desplazamiento, transiciones)
    │       ├── css/settings.css                 (página de Ajustes, diseño de tarjetas agrupadas)
    │       └── js/measure.js                    (medición de desbordamiento + callback de progreso del desplazamiento)
    ├── NewsBoard.Maui/              (net10.0-android; net10.0-windows10.0.19041.0)
    │   ├── NewsBoard.Maui.csproj
    │   ├── MauiProgram.cs                       (DI: implementaciones de plataforma según el TFM, después AddNewsBoardCore)
    │   ├── NewsBoardBoot.cs                     (carga la configuración y la obtención al arrancar)
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainPage.xaml / MainPage.xaml.cs     (host de BlazorWebView)
    │   ├── wwwroot/index.html
    │   ├── Resources/
    │   │   ├── AppIcon/appicon.svg, appiconfg.svg
    │   │   └── Splash/splash.svg
    │   ├── Platforms/Android/
    │   │   ├── AndroidManifest.xml              (INTERNET, WAKE_LOCK, POST_NOTIFICATIONS, RECEIVE_BOOT_COMPLETED, servicio en primer plano)
    │   │   ├── MainActivity.cs / MainApplication.cs
    │   │   ├── AndroidScreenController.cs       (mantener pantalla encendida + brillo de ventana 0 + restauración)
    │   │   ├── AndroidSpeechService.cs          (Android.Speech.Tts nativo + UtteranceProgressListener)
    │   │   ├── MauiSecretStore.cs               (SecureStorage)
    │   │   ├── AndroidAutoStart.cs, AutoStartHelper.cs, BootReceiver.cs, NewsBoardForegroundService.cs
    │   │   └── AndroidLinkOpener.cs
    │   └── Platforms/Windows/                   (cabecera de Windows: los mismos componentes Razor en el escritorio)
    │       ├── App.xaml / App.xaml.cs
    │       ├── app.manifest
    │       ├── WindowsScreenController.cs       (SC_MONITORPOWER + guardas de primer plano/pantalla completa)
    │       ├── WindowsSpeechService.cs
    │       ├── WindowsSecretStore.cs
    │       ├── WindowsAutoStart.cs
    │       └── WindowsLinkOpener.cs
    ├── NewsBoard.Wpf/               (net10.0-windows10.0.19041.0)
    │   ├── NewsBoard.Wpf.csproj
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainWindow.xaml / MainWindow.xaml.cs (BlazorWebView aloja Routes; F11 / Esc para pantalla completa sin bordes)
    │   ├── wwwroot/index.html                   (Blazor.start() explícito + aviso de arranque visible)
    │   └── Services/
    │       ├── WpfScreenController.cs
    │       ├── WpfSpeechService.cs              (System.Speech.Synthesis.SpeechSynthesizer)
    │       ├── WpfSecretStore.cs                (DPAPI ProtectedData)
    │       ├── WpfAutoStart.cs                  (clave Run de HKCU)
    │       └── WpfLinkOpener.cs
    └── NewsBoard.Tests/             (net10.0, xunit — 24 archivos .cs: suites Sa1–Sa10 por área, más suites específicas y los dobles/colecciones compartidos)
        └── NewsBoard.Tests.csproj
```

## Un flujo inicio → obtención → reproducción

1. **Arranque del host.** El host de WPF registra sus implementaciones de plataforma en `App.xaml.cs`; el host de MAUI registra el conjunto de Android o el de Windows en `MauiProgram.cs` (según el framework de destino). Después, ambos llaman a `AddNewsBoardCore()`. `MainWindow` (WPF) y `MainPage` (MAUI) alojan un `BlazorWebView` cuyo componente raíz es `Components/Routes.razor`.
2. **Ensamblado.** `AddNewsBoardCore()` registra un único `HttpClient` con `Timeout.InfiniteTimeSpan`: las tres implementaciones de red llevan su propio tiempo (15 s / 90 s / 3 s), así que un tiempo de espera del `HttpClient` las cortaría antes de tiempo. También registra `IClock → SystemClock`, `NewsRepository` (expuesto como `INewsRepository`), `ConfigStore` (expuesto como `IConfigProvider`), `NewsSummarizer`, `ImageResolver`, `ArticleContentFetcher`, las dos implementaciones de `INewsSource`, `NewsFetchCoordinator`, `NewsFetchScheduler` y `PlaybackEngine`.
3. **Inicialización.** `InitializeNewsBoardAsync()` carga `config.json` (incluida la clave de API leída de `ISecretStore`), carga `news.json` y luego arranca `NewsFetchScheduler`: una obtención inmediata y después un bucle con el «intervalo de obtención» (5 minutos por defecto). **Si esta llamada no se espera con await, la lista de reproducción queda vacía y el panel se queda para siempre en «正在获取新闻…».**
4. **Obtención.** Planificador → `NewsFetchCoordinator` → `INewsSource` (la fuente RSS de canal, o el canal de búsqueda web del modelo; «Auto» prueba primero la búsqueda web y recurre a RSS si falla) → deduplicación por URL → obtención del cuerpo del artículo → `NewsSummarizer` (resumen + importancia + palabra clave) → `ImageResolver` (solo en modo texto + imagen) → caché de `NewsRepository`.
5. **Reproducción.** `Display.razor` vuelve a esperar `InitializeNewsBoardAsync()`, arma la compuerta del primer fotograma y llama a `PlaybackEngine.StartAsync()`, que activa el mantener pantalla encendida, aplica la preferencia de pantalla completa, sale del apagado (blackout), sondea el motor de voz, recarga la lista de reproducción y entra en el bucle SHOW → BLACKOUT → SHOW.
6. **Renderizado.** `NewsCard.razor` renderiza el elemento actual; `wwwroot/js/measure.js` mide el desbordamiento y controla el desplazamiento; `ISpeechService` lee el resumen en voz alta y su callback de finalización termina ese elemento (decisión D5 — la lectura es el reloj del elemento). `IScreenController` se encarga de mantener la pantalla encendida y del apagado.

## Cómo se inyectan las capacidades de plataforma

`NewsBoard.Shared.Services.Abstractions` define las interfaces; cada host registra sus propias implementaciones en la inyección de dependencias **antes** de llamar a `AddNewsBoardCore()`.

| Interfaz | WPF | MAUI (Android) | MAUI (Windows) |
|---|---|---|---|
| `IScreenController` | `WpfScreenController` | `AndroidScreenController` | `WindowsScreenController` |
| `ISpeechService` | `WpfSpeechService` | `AndroidSpeechService` | `WindowsSpeechService` |
| `ISecretStore` | `WpfSecretStore` | `MauiSecretStore` | `WindowsSecretStore` |
| `IAutoStart` | `WpfAutoStart` | `AndroidAutoStart` | `WindowsAutoStart` |
| `ILinkOpener` | `WpfLinkOpener` | `AndroidLinkOpener` | `WindowsLinkOpener` |
| `INewsSource`, `INewsRepository`, `IConfigProvider`, `IClock` | los registra una sola vez `AddNewsBoardCore()` en Shared — sin implementación de plataforma | | |

`NewsBoard.Maui` elige el conjunto de Android o el de Windows según el framework de destino dentro de `MauiProgram.cs`; esa compilación condicional se queda en el proyecto host. Shared debe permanecer libre de `#if ANDROID` y `#if WINDOWS`.

## Contratos congelados

Estos archivos y firmas no se pueden cambiar (añadir un miembro `partial` en un archivo propio, o privatizar un detalle de implementación, sí es aceptable):

- `NewsBoard.Shared.Models.NewsItem` y `RawNewsItem` — incluidos `NewsItem.Importance` (`double?`, 0–1, `null` = sin puntuar) y `NewsItem.RawContent` (el contenido bruto obtenido), más `RawNewsItem.Importance` y `RawNewsItem.Description`.
- `NewsBoard.Shared.Services.AppConfig` y todos sus enums — incluidos `SummaryLanguage` (`SummaryLanguageOption`: SimplifiedChinese por defecto / TraditionalChinese / English, con `SummaryLanguageText.PromptRule()` para la restricción del prompt y `DisplayName()` para el texto de la interfaz), `ClockFontSizeSp` (32 por defecto), `ClockFontFamily` (`""` por defecto), `ImportanceThreshold` (0,7 por defecto) y `RetentionDays` (3 por defecto); `PlayOrder` es **Random** por defecto.
- `NewsBoard.Shared.Services.Abstractions`: `IScreenController`, `ISpeechService`, `ISecretStore`, `IAutoStart`, `ILinkOpener`, `INewsSource`, `IConfigProvider`, `INewsRepository`, `IClock` (con `SystemClock`).
- La API de control manual de `PlaybackEngine`: `NextAsync()` / `PreviousAsync()` / `InterruptAsync()` (interrumpe esta ronda → apagado durante los segundos de «duración del apagado» → siguiente ronda; se apaga una vez incluso con el ciclo de apagado desactivado), con vuelta circular en ambos sentidos; y `WakeAsync()` (durante el apagado, un toque vuelve al panel y reinicia la ventana desde el elemento actual; mientras se muestra no tiene efecto secundario: ni cambia de elemento ni reinicia la ventana).
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` y `InitializeNewsBoardAsync()` — el punto de ensamblado único para los hosts.
- El conjunto de `using` de `_Imports.razor`, y `Components/Routes.razor` como `BlazorWebView.RootComponent`.
