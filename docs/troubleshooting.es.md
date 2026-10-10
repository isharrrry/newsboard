# Solución de problemas

[English](troubleshooting.md) | [中文](troubleshooting.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

Cada entrada está escrita como **síntoma → causa → solución**, y cada solución es un comando que puedes ejecutar o una ruta que puedes abrir. Todo lo marcado como *medido* se observó en la máquina de este repositorio o en el dispositivo de prueba, no se dedujo.

Las entradas están agrupadas por plataforma: **General (cualquiera de los tres hosts)** → **Android** → **Windows**. Se conservan los números de entrada originales, así que las referencias cruzadas del tipo «ver la entrada 9» siguen siendo válidas.

---

## General (cualquiera de los tres hosts)

### 1. Pantalla negra con `#app` vacío (la página host de MAUI arranca Blazor demasiado pronto)

**Aplica a** — hosts de MAUI: `NewsBoard.Maui` en Android y su encabezado Windows.

**Síntoma** — En el host de MAUI el proceso sigue vivo y el WebView ocupa toda la pantalla, pero no se renderiza nada: `#app.innerHTML.length === 0`. En `logcat` o en la consola aparece `Uncaught (in promise) Error: Blazor has already started.`

**Causa** — La página host de MAUI (`src/NewsBoard.Maui/wwwroot/index.html`) arrancó Blazor antes que el framework. El `BlazorWebView` de MAUI arranca Blazor por su cuenta y registra el componente raíz declarado en `MainPage.xaml` (`Selector="#app"`); un script de la página que llama primero a `Blazor.start()` arranca Blazor *sin* componente raíz, así que el arranque posterior del framework se rechaza y `#app` queda vacío. Medido en el dispositivo Android: `pidof` devuelve un valor, `uiautomator` muestra el WebView ocupando la pantalla y la comprobación por CDP devuelve `#app.innerHTML.length === 0`.

**Solución** — La página host nunca debe llamar a `Blazor.start()` antes que el framework. Solo observa: un temporizador vigila si `#app` tiene nodos hijos (entonces elimina el elemento `#boot-hint` y termina), y recurre a `Blazor.start()` únicamente si `#app` sigue vacío después de 3 segundos. La prueba de regresión es `src/NewsBoard.Tests/Sa5MauiHostPageTests.cs`, que verifica que `Blazor.start()` aparece después de la llamada a `setInterval` (antes se eliminan los comentarios de línea). La página host de WPF es otro archivo y no se ve afectada.

---

### 2. Se queda en el marcador de "obteniendo noticias"

**Síntoma** — La página del panel nunca abandona el marcador `正在获取新闻…`; en el peor caso el proceso no muestra ninguna conexión de red.

**Causa** — Cuatro causas conocidas, en el orden en que conviene comprobarlas:

1. No hay ninguna clave de API configurada, así que fallan tanto la obtención como el resumen.
2. El host de MAUI nunca llamó a `InitializeNewsBoardAsync()`, así que la configuración (incluida la clave) no se carga y el planificador de obtención no arranca.
3. Todas las fuentes de la cadena RSS no están disponibles. Medido el 2026-10-08: Bing `?format=RSS` devuelve `text/html`, Baidu `newsrss` devuelve una página de verificación de seguridad y Google News RSS agota el tiempo de espera en esta máquina.
4. Todos los elementos en caché se descartaron por el umbral de importancia — ver la entrada 9.

**Solución** —

- Clave: página de Ajustes → grupo **API / obtención** → pega la clave. En Android se guarda en `SecureStorage`.
- Host de MAUI: `MauiProgram.CreateMauiApp()` debe llamar a `InitializeNewsBoardAsync()`; `Display.razor` también lo espera antes de reproducir como red de seguridad (idempotente).
- Fuentes: revisa `RssTemplate` / `RssFallbackTemplates` en la página de Ajustes. Fuentes de canal medidas como funcionales aquí: `https://www.chinanews.com.cn/rss/{scroll-news,china,world}.xml`, `http://www.people.com.cn/rss/{politics,world}.xml`, `https://www.ithome.com/rss/`.
- Umbral: ver la entrada 9.

---

### 6. La misma imagen aparece en varios elementos, o un elemento no muestra imagen

**Síntoma** — Medido antes del arreglo: 9 de 9 elementos tenían imagen, pero 3 grupos compartían una misma imagen de plantilla (5 / 4 / 4 elementos). Después del arreglo: 5 de 9 elementos tienen imagen y no hay grupos duplicados.

**Causa** — Dos problemas distintos. Primero, las páginas de artículo de chinanews.com.cn y people.com.cn no traen ni `og:image` ni `twitter:image`, y sus fuentes no traen `enclosure` / `media:*`, así que un resolutor que solo mira los metadatos cae en la misma imagen de plantilla del sitio. Segundo, nada impedía que una URL de imagen se asignara a varios elementos.

**Solución** — `ImageResolver` prueba cuatro niveles — `og:image` → `twitter:image` → `link[rel=image_src]` → la primera imagen dentro del cuerpo del artículo (prefiriendo el contenedor del artículo, después de un `<p>`, y descartando logotipos, iconos, marcadores de posición y recursos de plantilla con más de 60 días de antigüedad respecto a la fecha del artículo) — y valida cada candidato con una petición Range que debe devolver realmente `image/*` (el servidor de imágenes de chinanews tiene protección contra hotlinking y si no devuelve HTML). `ImageResolver.DedupeImages` junto con `ApplyImagesAsync(..., reservedImageUrls)` garantizan que una imagen pertenezca a un único elemento; un elemento que no consigue una imagen exclusiva se trata como "el artículo no tiene imagen" y no muestra ninguna.

---

### 7. La etiqueta de palabra clave muestra el nombre del canal

**Síntoma** — La etiqueta de la parte superior de la página del panel muestra un nombre de canal, por ejemplo `中新网即时新闻`, en lugar de una de tus palabras clave configuradas.

**Causa** — El `RssTemplate` por defecto es una fuente de canal compartida (no contiene `{q}`), así que una sola petición HTTP devuelve el canal completo y los elementos llegan etiquetados con el nombre del canal. `KeywordClassifier.Classify` elige la primera palabra clave configurada que encuentre en el título y luego en el cuerpo, y solo recurre al nombre del canal cuando no coincide ninguna. Medido: con `Keywords=武汉/AI` y la fuente de desplazamiento de chinanews, los 9 elementos de ese día eran noticias del Día Nacional / Japón / bolsa A que no contienen ninguna de las dos palabras, y el modelo también devolvió un `keyword` vacío. **Esto es "ninguna coincidencia", no "se perdió la clasificación".**

**Solución** — No hay nada roto. Añade las palabras que tu fuente realmente usa, o elige una fuente que coincida con tus palabras clave. El filtro "mostrar solo los elementos que coinciden con una palabra clave" todavía no existe; por ahora una palabra clave es solo una etiqueta de agrupación.

---

### 8. Los resúmenes contienen caracteres chinos tradicionales

**Síntoma** — Los resúmenes que devuelve el canal de búsqueda web contienen caracteres tradicionales. Medido antes del arreglo: 6 de 9 elementos tenían 1–3 caracteres tradicionales.

**Causa** — El prompt no fijaba el idioma de salida, y ese canal a veces recupera fuentes de Hong Kong / Taiwán.

**Solución** — `AppConfig.SummaryLanguage` (por defecto `SimplifiedChinese`) se escribe en ambos prompts mediante `SummaryLanguageText.PromptRule()`. Medido después del arreglo: las mismas palabras clave produjeron 0 caracteres tradicionales en 9 elementos. Ten en cuenta que cambiar el idioma **no** reescribe los resúmenes en caché — borra `news.json` o espera a que venza la retención por día natural.

---

### 9. La lista de reproducción queda vacía tras filtrar por umbral

**Síntoma** — Con un `ImportanceThreshold` alto, la página del panel sigue mostrando el marcador de obtención aunque `news.json` contenga elementos.

**Causa** — `NewsRepository.GetPlaylist` filtra primero por importancia y ordena después. Si no pasa nada, la lista queda vacía. Eso es semántica de configuración, no un cuelgue.

**Solución** — Baja `ImportanceThreshold` en la página de Ajustes (por defecto 0.7). El código además tiene una red de seguridad: cuando la lista filtrada está vacía añade un elemento aleatorio de la caché (una copia, así que la caché no se modifica) y marca ese resumen con un ` ~` final. Medido con el umbral puesto temporalmente en 0.95, el panel mostró `…移送检察机关审查起诉。 ~`.

---

### 10. Las pruebas sensibles al tiempo fallan de forma intermitente

**Síntoma** — `Sa3PlaybackEngineTests` / `PlaybackControlTests` fallan de vez en cuando en una máquina cargada (medido antes del arreglo: 3 de 4 ejecuciones completas en rojo), por ejemplo `NextAsync_SkipsToFollowingItem`.

**Causa** — Esas pruebas impulsan un `ScaledClock` a 100× mientras `WaitUntilAsync` sondea cada 5 ms de tiempo real, que son 0.5 s de tiempo del motor; una máquina cargada se pasa de un punto de control antes de que la aserción lo lea.

**Solución** — Ya está corregido: primer resumen más largo (unos 40 caracteres), puntos de control movidos hacia dentro de la ventana (59 s → 55 s, 365 s → 362 s), tolerancia ampliada de 5 s a 20 s y un `Task.Delay(250)` antes de tomar la línea base. Medido después del arreglo: 5 ejecuciones completas consecutivas en verde, 220/220 cada una. Si añades una aserción temporal nueva, mantén los mismos márgenes.

---

### 11. Un script sobrescribió tu directorio de datos real

**Síntoma** — Tus palabras clave u otros ajustes cambian después de ejecutar un script automático. Esto ocurrió de verdad: 4 scripts e2e copiaron una configuración de prueba sobre el `config.json` real del usuario, sustituyendo 6 palabras clave por `武汉/AI`.

**Causa** — Los scripts escribían en el directorio de datos real (`%LOCALAPPDATA%\NewsBoard`), y el `config-backup.json` que dejaban se copió *después* de la sobrescritura, así que tenía el mismo MD5 que el archivo dañado y nunca pudo revertir nada.

**Solución** — Ejecuta cualquier script que toque la obtención real con un directorio aislado:

```powershell
$env:NEWSBOARD_DATA_DIR = "D:\path\to\isolated-dir"
```

`NewsBoardDataDirectory.Resolve()` es el único punto de resolución compartido por `ConfigStore` y `NewsRepository`, y un `NEWSBOARD_DATA_DIR` no vacío tiene prioridad. Además, `ConfigStore` conserva la versión anterior como `config.json.bak` antes de cada sobrescritura, así que una escritura errónea se puede revertir restaurando ese archivo.

---

### 14. Todas las filas de la página de Ajustes se aprietan en una sola línea

**Síntoma** — Después de un cambio de CSS, todas las filas de la página de Ajustes se colapsan en una sola línea.

**Causa** — La página de Ajustes reutilizó el nombre de clase `.card`, que en `app.css` es la tarjeta de noticias de la página del panel (`display:flex`).

**Solución** — El contenedor de grupo de la página de Ajustes debe ser `.settings-card` y nunca debe usar `class="card"`. Prueba de regresión: `Sa2SettingsPageTests.Settings_UsesGroupedCardLayout_AndCancelIsBackLink`. Esto se localizó volcando el HTML real con `HtmlRenderer` y leyendo los estilos calculados con Edge `--headless --dump-dom`.

---

## Android

### 3. Android: el APK de Debug falla justo después de `adb install`

**Síntoma** — `adb install` del `*-Signed.apk` de Debug funciona, pero la aplicación aborta al arrancar:

```
Fatal signal 6 (SIGABRT)
Abort message: 'No assemblies found in /data/user/0/com.newsboard.app/files/.__override__/arm64-v8a
... Assuming this is part of Fast Deployment. Exiting...'
```

**Causa** — Las compilaciones Debug usan **Fast Deployment** de .NET Android: los ensamblados no están dentro del APK, el destino de implementación los envía al dispositivo a `files/.__override__/<abi>`. Un `adb install` normal se salta ese paso.

**Solución** — Implementa con el destino de MSBuild:

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

`-t:Run` también funciona y además inicia la aplicación. Para un APK autocontenido que puedas distribuir, compila en Release. Para confirmar que los ensamblados se enviaron: `adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l` (medido: 440 archivos).

---

### 4. Android: el inicio automático al arrancar no hace nada (MIUI)

**Síntoma** — Después de reiniciar, la aplicación no está en marcha: `adb shell pidof com.newsboard.app` está vacío, no hay notificación residente y `dumpsys activity services com.newsboard.app` no muestra nada.

**Causa** — La ruta de código es correcta; MIUI la bloquea. Medido en un Xiaomi `M2011K2C` (Android 13): `dumpsys activity broadcasts` muestra que `BOOT_COMPLETED` **sí** se entrega al receptor (`Deliver 0 #154: (manifest)` → `name=crc64017365ed8da6996e.BootReceiver`), y el registro del sistema anota inmediatamente:

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED … }: process is not permitted to auto start
```

`cmd appops get com.newsboard.app 10021` devuelve `MIUIOP(10021): ignore`.

**Solución** — Permítelo a mano: Ajustes → Aplicaciones → Administrar aplicaciones → NewsBoard → **Inicio automático** activado; ajusta también la política de batería a *Sin restricciones*. Después vuelve a probar con `adb reboot` y comprueba `pidof com.newsboard.app` y `dumpsys activity services com.newsboard.app` (debería aparecer `NewsBoardForegroundService isForeground=true foregroundId=1001`). Sin root no puedes hacerlo desde adb: `cmd appops set com.newsboard.app 10021 allow` devuelve `No operations.` y `am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` falla con `Error type 3 … does not exist`.

---

### 5. Android: la fase de apagado no apaga el panel

**Síntoma** — Durante el apagado la pantalla está negra, pero la retroiluminación sigue claramente encendida.

**Causa** — Es una decisión de diseño, no un defecto. `AndroidScreenController.SetBlackoutAsync(true)` pone el brillo de la ventana a `0f` y pinta una capa negra a pantalla completa; la retroiluminación física permanece encendida. Android no ofrece a una aplicación normal ninguna forma sin permisos de apagar el panel: `PowerManager.GoToSleep()` necesita el permiso de firma `DEVICE_POWER`, y `DevicePolicyManager.LockNow()` necesita derechos de administrador de dispositivos y además bloquea el dispositivo, así que despertar exigiría desbloquearlo. Medido: `mScreenBrightnessOverrideFromWindowManager` pasa de `NaN` a `0.0` y la captura baja de 300,867 bytes a 27,787 bytes.

**Solución** — No hay nada que arreglar; trátalo como limitación conocida. El "mantener pantalla encendida" se sostiene con el indicador de ventana `KEEP_SCREEN_ON`, que la fase de apagado deliberadamente no retira — retirarlo dejaría que el sistema apagara el panel tras `screen_off_timeout` (medido en este dispositivo: 600000 ms). Los dos hosts de Windows sí apagan el monitor, mediante `SC_MONITORPOWER`.

---

## Windows

### 12. MAUI en Windows arranca con una ventana negra y se cierra

**Síntoma** — Después de matar procesos de WebView2 de forma global, cada arranque del host MAUI en Windows es una ventana negra que se cierra sola.

**Causa** — Un `Stop-Process -Name msedgewebview2` global también mata el WebView2 de una instancia recién creada y **corrompe su directorio de datos de usuario**.

**Solución** — Borra el directorio de datos dañado y vuelve a arrancar:

```powershell
Remove-Item -Recurse -Force "src\NewsBoard.Maui\bin\...\NewsBoard.Maui.exe.WebView2"
```

Al limpiar, mata el proceso de la aplicación en sí, no todos los `msedgewebview2` de la máquina.

---

### 13. Un script `.ps1` con texto en chino no se puede analizar

**Síntoma** — Un script de verificación con caracteres chinos no se puede analizar; las cadenas quedan cortadas por la mitad.

**Causa** — Windows PowerShell 5.1 lee los archivos `.ps1` como ANSI, así que un archivo UTF-8 sin BOM se descodifica mal.

**Solución** — O mantienes el script en ASCII puro, o lo escribes con BOM:

```powershell
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
```

La misma trampa afecta a los archivos de código: nunca hagas ida y vuelta de un `.cs` con `Get-Content` / `Set-Content`, porque se descodifica como ANSI y puede dejar el archivo como UTF-8 inválido. Esto le ocurrió una vez a un `.cs` con comentarios en chino.

---

### 15. El host de WPF falla al arrancar: `Microsoft.Windows.SDK.NET`

**Síntoma** — El host de WPF muere de inmediato con `FileNotFoundException: Microsoft.Windows.SDK.NET`.

**Causa** — El proyecto usaba el framework de destino pelado `net10.0-windows`. `BlazorWebView` usa internamente `WebView2CompositionControl` (composición D3D) y necesita los ensamblados de proyección de CsWinRT, que solo proporciona el framework de destino con versión.

**Solución** — Usa `net10.0-windows10.0.19041.0` para el proyecto WPF.

---

Relacionado: [Contribuir](contributing.es.md) · [Notas de Android](android.es.md) · [Reproducción](playback.es.md) · [Flujo de noticias](news-pipeline.es.md) · [Índice de documentación](index.es.md)
