# Configuración y datos

[English](configuration.md) | [中文](configuration.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

## Dónde están los archivos

| Host | Configuración | Caché de noticias | Clave de API |
|---|---|---|---|
| WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat` (DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`; en disco es texto cifrado) |
| MAUI (Windows) | el mismo archivo que WPF — los dos hosts lo comparten por completo | el mismo archivo | el mismo archivo (la clave sirve en ambos hosts) |
| MAUI (Android) | directorio privado de la app (`LocalApplicationData`, paquete `com.newsboard.app`) | el mismo directorio | MAUI `SecureStorage` (Android Keystore; no es un archivo en texto plano) |

- **Cambiar el directorio de datos**: define la variable de entorno `NEWSBOARD_DATA_DIR` (cualquier valor no vacío tiene prioridad). `ConfigStore` y `NewsRepository` comparten este único punto de resolución. Los scripts automáticos o de sondeo **deben** apuntarla a un directorio aislado y nunca al real: un script de sondeo llegó a sobrescribir las 6 palabras clave del usuario con 2 (ver `plans/news-board.plan.md` §13).
- **Revertir una escritura errónea**: antes de cada sobrescritura, `ConfigStore` copia la versión anterior a `config.json.bak`.
- **`ApiKey` nunca llega al JSON**: el campo lleva `[JsonIgnore]` y se inyecta desde el almacén seguro de la plataforma bajo el nombre `ConfigStore.ApiKeySecretName` (`deepseek-api-key`).
- **Cómo entra la clave en Android**: `secret.dat` está cifrado con DPAPI para el usuario actual de Windows y está atado a esa máquina, así que copiarlo al teléfono no sirve de nada. Vuelve a pegar la clave de API de DeepSeek en el dispositivo, en Ajustes → API / obtención.
- **Inicio automático al arrancar** (Windows) se escribe en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Página de Ajustes

`Settings.razor` se presenta como una lista agrupada al estilo iOS (`.settings-card`). La barra superior y la barra de acciones inferior usan `position:fixed` contra el viewport real.

| Grupo | Qué contiene |
|---|---|
| **Modo de visualización** | modo de reproducción (solo texto / texto + imagen) y el interruptor derivado «mostrar imágenes», que es el mismo interruptor visto dos veces |
| **API / obtención** | modo de obtención, BaseUrl / Model / endpoint de búsqueda y max_uses, clave de API, lista de palabras clave, elementos por palabra clave, intervalo de obtención, longitud del resumen, idioma del resumen, intervalo mínimo de IA, fuente RSS y alternativas, solo noticias de hoy, umbral de importancia, días de retención |
| **Apariencia** | colores de fondo / texto / acento, tamaño y peso de la fuente del cuerpo, altura de línea, margen interior, tamaño de la etiqueta de palabra clave, alineación, mostrar reloj, tamaño y fuente del reloj, mostrar palabra clave / título / fuente |
| **Reproducción / lectura en voz alta** | lectura activada, contenido leído, idioma, velocidad, tono, silencio previo, franja silenciosa, política cuando el TTS no está disponible, duración de pantalla encendida, permanencia por elemento, velocidad de desplazamiento, duración del apagado (blackout), ciclo de apagado, modo de apagado, comportamiento al terminar la ventana, posición de la imagen y porcentaje de pantalla, orden de reproducción, continuar entre rondas |
| **Sistema / host** | mantener pantalla encendida, inicio automático al arrancar, obtener solo por WiFi, pantalla completa sin bordes en Windows, mostrar barra de estado en Android |

Barra inferior: **Guardar / Cancelar / Restaurar valores predeterminados**. «Restaurar valores predeterminados» solo restablece los valores que se están editando en esta página — no borra la clave de API guardada — y nada surte efecto hasta pulsar Guardar. La fila del inicio automático lee el estado real de la plataforma a través de `IAutoStart`, y el grupo incluye además una autoprueba de «apagado durante 2 segundos» que no toca la configuración.

Los ids de los controles coinciden con los nombres de campo en camelCase: `playbackMode`, `showImages`, `summaryLanguage`, `summaryMaxChars`, `importanceThreshold`, `retentionDays`, `clockFontSize`, `clockFontFamily`, `speechEnabled`, `showDuration`, `blackoutDuration`, `winFullscreen`, etc.

## Referencia de configuración

Los nombres de campo son los identificadores de `src/NewsBoard.Shared/Services/AppConfig.cs`, y los valores predeterminados son los de ese archivo. `plans/news-board.plan.md` §4 lista los mismos elementos en los mismos cuatro grupos.

### API / obtención

| Campo | Valor predeterminado | Significado |
|---|---|---|
| `FetchMode` | `Auto` | `RssThenSummarize` / `WebSearch` / `Auto`. `Auto` prueba primero el canal de búsqueda web y recurre a RSS si falla |
| `BaseUrl` | `https://api.deepseek.com` | endpoint compatible con OpenAI que usa el canal de resumen por RSS (`POST /chat/completions`) |
| `Model` | `deepseek-chat` | nombre del modelo, compartido por los dos canales (el backend devuelve `deepseek-v4-flash`) |
| `BaseUrlSearch` | `https://api.deepseek.com/anthropic` | endpoint compatible con Anthropic que usa el canal de búsqueda web (`POST /v1/messages` con la herramienta `web_search`) |
| `SearchMaxUses` | `3` | cuántas veces puede el modelo llamar a la herramienta de búsqueda por palabra clave |
| `ApiKey` | vacío | vive solo en el almacén seguro de la plataforma, nunca en el JSON |
| `Keywords` | `武汉`, `中国`, `美国`, `台湾`, `国际`, `AI` | lista de palabras clave; su orden es el que usa `PlayOrder.ByKeywordOrder`, y al guardar se quitan vacíos, duplicados y espacios sobrantes |
| `ItemsPerKeyword` | `3` | cuántos elementos tomar por palabra clave |
| `FetchIntervalMinutes` | `5` | periodo de obtención programada |
| `SummaryMaxChars` | `70` | restricción blanda del prompt (escribir 55–70 caracteres); los dos canales pasan además por la comprobación local `SummaryText.Normalize`, que corta en 1,2× (84 caracteres) hasta el último punto, porque se midió que el modelo se pasa |
| `SummaryLanguage` | `SimplifiedChinese` | `SimplifiedChinese` / `TraditionalChinese` / `English`; se escribe en los prompts de ambos canales como regla dura |
| `AiMinIntervalMinutes` | `10` | intervalo mínimo entre llamadas a la IA, para que una fuente inestable no queme tokens dos veces |
| `RssTemplate` | `https://www.chinanews.com.cn/rss/scroll-news.xml` | una plantilla con `{q}` se obtiene una vez por palabra clave; sin `{q}` es una fuente RSS de canal compartida y se obtiene una sola vez |
| `RssFallbackTemplates` | `http://www.people.com.cn/rss/politics.xml`, `https://www.ithome.com/rss/` | se prueban en orden cuando falla la fuente principal |
| `OnlyTodayNews` | `true` | descarta elementos publicados hace más de 24 horas (los que no traen fecha se conservan) |
| `ImportanceThreshold` | `0.7` | solo se reproducen los elementos que el modelo puntuó en este valor o por encima |
| `RetentionDays` | `3` | retención de la caché, contada en días naturales |

### Apariencia

| Campo | Valor predeterminado | Significado |
|---|---|---|
| `BackgroundColor` | `#000000` | fondo del panel |
| `ForegroundColor` | `#FFFFFF` | texto del cuerpo |
| `AccentColor` | `#00D4AA` | etiqueta de palabra clave y reloj |
| `FontSizeSp` | `56` | tamaño de la fuente del cuerpo |
| `FontWeight` | `Bold` | `Normal` / `Bold` |
| `LineHeight` | `1.35` | múltiplo de altura de línea |
| `PagePaddingPx` | `48` | margen interior de la página |
| `KeywordLabelFontSizeSp` | `28` | tamaño de la etiqueta de palabra clave |
| `TextAlign` | `Center` | `Left` / `Center` / `Right` |
| `ShowClock` | `true` | mostrar el reloj |
| `ClockFontSizeSp` | `32` | tamaño del reloj; ya no se deriva del tamaño de la etiqueta de palabra clave |
| `ClockFontFamily` | vacío | vacío significa seguir la fuente predeterminada del sistema |
| `ShowKeywordLabel` | `true` | mostrar la etiqueta de palabra clave |
| `ShowTitle` | `false` | desactivado por defecto: el título y el resumen suelen solaparse, así que mostrar ambos repite lo mismo en la misma pantalla |
| `ShowSource` | `false` | mostrar la fuente de la noticia |

### Reproducción / lectura en voz alta

| Campo | Valor predeterminado | Significado |
|---|---|---|
| `PlaybackMode` | `TextOnly` | `TextOnly` / `TextAndImage`. La página de Ajustes lo muestra en el primer grupo («Modo de visualización») junto con el interruptor derivado `showImages` |
| `SpeechEnabled` | `true` | con la lectura desactivada, el motor recurre a la permanencia fija por elemento |
| `SpeechContent` | `SummaryOnly` | `SummaryOnly` / `TitleAndSummary` |
| `SpeechRate` | `1.0` | 0,5–2,0 |
| `SpeechPitch` | `1.0` | 0,5–2,0 |
| `SpeechLanguage` | `zh-CN` | se pasa al paquete de voz del TTS del sistema |
| `SpeechLeadSeconds` | `1` | silencio tras cambiar de elemento, antes de empezar a leer |
| `QuietHours` | vacío | por ejemplo `23:00-07:00`; admite rangos que cruzan la medianoche. Dentro de ese rango el panel muestra pero no lee en voz alta; vacío significa audible todo el día |
| `TtsUnavailable` | `FallbackHold` | `FallbackHold` (permanencia fija) / `SkipItem` |
| `ShowDurationSeconds` | `60` | duración de una ventana de visualización |
| `ItemHoldSeconds` | `12` | solo surte efecto con la lectura desactivada |
| `ScrollSpeedPxPerSecond` | `60` | solo surte efecto con la lectura desactivada; con la lectura activada la velocidad de desplazamiento sigue a la duración de la lectura |
| `BlackoutDurationSeconds` | `300` | cuánto dura el apagado (blackout) |
| `BlackoutCycleEnabled` | `true` | desactivado significa que la pantalla permanece encendida; una interrupción manual aún apaga una vez |
| `BlackoutMode` | `BrightnessZeroAndBlack` | `BrightnessZeroAndBlack` / `BlackOnly` (conservar la retroiluminación) |
| `WindowEndBehavior` | `FinishCurrentItem` | `FinishCurrentItem` / `InterruptImmediately` |
| `ImagePosition` | `TopImageBottomText` | `TopImageBottomText` / `LeftImageRightText` |
| `ImageScreenPercent` | `40` | porcentaje de pantalla que ocupa la imagen en modo texto + imagen |
| `PlayOrder` | `Random` | `ByKeywordOrder` / `ByPublishTime` / `Random` |
| `ResumeAcrossRounds` | `true` | un elemento cortado por la ventana continúa en la siguiente ronda |

### Sistema / host

| Campo | Valor predeterminado | Significado |
|---|---|---|
| `KeepScreenOn` | `true` | surte efecto en Android; los hosts de escritorio lo implementan como no-op |
| `AutoStart` | `true` | inicio automático al arrancar; ver `plans/news-board.plan.md` §3.4 |
| `WifiOnlyFetch` | `false` | desactivado significa que se permite obtener por datos móviles |
| `WindowsBorderlessFullscreen` | `false` | desactivado significa una ventana normal con barra de título; se puede activar en Ajustes |
| `AndroidShowStatusBar` | `true` | activado mantiene visibles reloj y batería; desactivado significa pantalla completa inmersiva |

## Campos que se prestan a confusión

- **`ImportanceThreshold` (0.7)** — los elementos cuya importancia está por debajo se filtran *antes* de ordenar. Los elementos con `Importance == null` (una caché antigua o una llamada a la IA que falló) se conservan, de modo que una actualización no puede vaciar todo el almacén. Si el filtro deja la caché vacía, `GetPlaylist` vuelve a añadir un elemento aleatorio como **copia** con ` ~` al final de su resumen: ese elemento no superó el umbral. La clave de caché del orden aleatorio incluye el umbral, así que cambiarlo vuelve a barajar de inmediato.
- **`RetentionDays` (3)** — esto es agrupación por día natural, no una ventana deslizante. `3` conserva hoy más los dos días naturales anteriores, y la ventana empieza en la medianoche local del día conservado más antiguo (`RetentionWindow.EarliestKeptInstant`). La diferencia práctica: las 06:00 y las 23:00 ven el mismo límite de día, en lugar de que una de las dos corte el tercer día por la mitad.
- **`SummaryLanguage`** — el valor predeterminado es chino simplificado, y el mismo valor decide lo que se muestra y lo que se lee en voz alta. Cambiarlo **no** reescribe los resúmenes que ya están en la caché: borra `news.json` o espera a que expire el día natural. Medido: antes de que existiera la regla en el prompt, el canal de búsqueda web recogía caracteres tradicionales en 6 de 9 artículos; después, en 0 de 9.
- **`PlayOrder` (`Random`)** — el orden se aplica después del filtrado por umbral. `ByKeywordOrder` sigue el orden de la lista de palabras clave, `ByPublishTime` pone primero lo más reciente y `Random` se baraja una vez y se reutiliza hasta que cambian la caché o el umbral.
- **`OnlyTodayNews` frente a `RetentionDays`** — el primero filtra al obtener (todo lo anterior a 24 horas), el segundo depura la caché por día natural. Los dos están activados por defecto y hacen trabajos distintos.
- **Escribir la configuración desde un script** — define siempre antes `NEWSBOARD_DATA_DIR` apuntando a un directorio aislado, y recuerda que `config.json.bak` guarda la versión anterior si una escritura sale mal.
