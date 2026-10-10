# Canal de noticias

[English](news-pipeline.md) | [中文](news-pipeline.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

Esta página sigue a una noticia desde la fuente hasta el panel: obtención, deduplicación, extracción del cuerpo del artículo, resumen, puntuación de importancia, clasificación por palabra clave, imágenes, retención y qué ocurre cuando la lista de reproducción quedaría vacía. Todo lo que se describe aquí vive en `NewsBoard.Shared` (sin dependencias de plataforma), así que se comporta igual en los tres hosts: Android, `NewsBoard.Wpf` y la cabecera de Windows de `NewsBoard.Maui`.

## Dos canales

| Canal | Clase de origen | Qué devuelve | ¿Necesita API Key? |
|---|---|---|---|
| Fuente RSS de canal | `RssNewsSource` | título, enlace, hora de publicación, descripción corta | solo para el resumen con modelo |
| Canal de búsqueda web | `WebSearchNewsSource` | título, resumen, enlace, importancia, palabra clave (ya resumido) | sí |

`FetchMode` (`NewsFetchMode` en `AppConfig`) decide cuál se ejecuta:

| `FetchMode` | Comportamiento |
|---|---|
| `RssThenSummarize` | solo RSS; cada elemento nuevo se resume después con el modelo |
| `WebSearch` | solo el canal de búsqueda web |
| `Auto` (predeterminado) | primero búsqueda web; si no devuelve nada, se recurre al RSS con el aviso `…，已回退 RSS` |

Antes de cualquier petición, `NewsFetchCoordinator.FetchAsync` comprueba `IsWithinAiCooldown`. Dentro de la ventana de espera (`AiMinIntervalMinutes`, 10 por defecto) la ronda se omite con el aviso `距上次 AI 调用不足最小间隔，本轮跳过（下轮重新抓取）`.

## Fuente RSS de canal

- Valor predeterminado de `RssTemplate`: `https://www.chinanews.com.cn/rss/scroll-news.xml`.
- Si la plantilla contiene `{q}` es una fuente **de búsqueda**: una petición por palabra clave.
- Si no contiene `{q}` es una **fuente RSS de canal compartida**: una petición por ronda, tomando los `max(ItemsPerKeyword × palabras clave, 10)` elementos más recientes (`MinSharedItems = 10`). La etiqueta de palabra clave de cada elemento es el título del canal del feed.
- Si falla o llega vacía, se prueban por orden los respaldos de `RssFallbackTemplates`: `http://www.people.com.cn/rss/politics.xml` y luego `https://www.ithome.com/rss/`.
- `OnlyTodayNews` (predeterminado `true`) conserva solo los elementos de hoy.

Medido el 2026-10-08: en esta máquina **no hay ninguna fuente RSS de búsqueda por palabra clave utilizable** — Bing `?format=RSS` devuelve `text/html`, Baidu `newsrss` devuelve una página de verificación de seguridad y Google News RSS agota el tiempo de espera. La búsqueda por palabra clave la asume por tanto el canal de búsqueda web.

## Canal de búsqueda web

- `POST {BaseUrlSearch}/v1/messages`, con las cabeceras `x-api-key` y `anthropic-version: 2023-06-01`.
- Herramienta: `{"type":"web_search_20250305","name":"web_search","max_uses":N}`, donde `N = SearchMaxUses` (3 por defecto).
- La respuesta contiene bloques `server_tool_use` y `web_search_tool_result`; solo se analiza el bloque de texto.
- Una petición por palabra clave. El modelo debe devolver JSON estricto con `title` / `summary` / `url` / `importance` / `keyword`; los elementos sin enlace o sin resumen se descartan y cada palabra clave conserva como máximo `ItemsPerKeyword` elementos.

## Deduplicación y coste de IA

- Los elementos se deduplican por un hash de la URL del artículo (`NewsSummarizer.MakeId`); un elemento que ya está en la caché **nunca** se vuelve a enviar al modelo.
- Medido: dos rondas consecutivas → la segunda ronda hizo **0 llamadas de chat**.
- La obtención (cada `FetchIntervalMinutes`, 5 minutos por defecto) y el resumen (solo elementos nuevos) están desacoplados; `AiMinIntervalMinutes` es el freno de seguridad.

## Extracción del cuerpo del artículo

`ArticleContentFetcher` (sin paquetes de terceros, tiempo de espera de 5 s, toda excepción se convierte en `null`).

- `NewsFetchCoordinator.BackfillRawContentAsync` se aplica a los elementos nuevos de **ambos** canales: aquellos cuya `Description` es más corta que **300** caracteres, **como máximo 5 por ronda** (`MaxContentBackfill`) y con **concurrencia 2** (`ContentConcurrency`).
- El cuerpo extraído se escribe de vuelta en `RawNewsItem.Description`, de modo que tanto la entrada del resumen **como** el `RawContent` de la caché usan el cuerpo real del artículo; después el elemento se reclasifica con él.
- `ExtractMainText` devuelve `null` por debajo de **60** caracteres, así que las notas muy cortas vuelven a la descripción del RSS.
- Todo lo que sigue a `(完)` / `（完）` se descarta, y las líneas finales sin punto de hasta 45 caracteres se tratan como ruido.
- Medido el 2026-10-08: tras una reconstrucción, 4 de 9 elementos tenían `RawContent` de más de 300 caracteres (302 / 620 / 676 / 868); una sonda independiente contra sitios reales tuvo **9/9 éxitos** (chinanews 137–720 caracteres, ITHome 332/944 caracteres), sin caracteres corruptos ni tiempos agotados.

## Resumen con modelo

- `NewsSummarizer` hace `POST {BaseUrl}/chat/completions` (compatible con OpenAI; `System.Text.Json`, sin SDK de terceros).
- Una petición por palabra clave, que cubre todo el lote de artículos de esa palabra clave.
- Restricciones del prompt: usar solo el texto de origen y no inventar cifras, fechas ni nombres; cada resumen ≤ `SummaryMaxChars` (**70** por defecto), en 1–2 frases y sin repetir el título; si la información es insuficiente el modelo debe devolver `summary` vacío y el elemento se omite.
- Ruta de fallo: tiempo agotado → un reintento → si sigue fallando, se degrada al título original truncado. `MaxAttempts = 2`.
- Segunda pasada local: `SummaryText.Normalize` trunca en el último punto lo que supere **1.2 ×** `SummaryMaxChars`.
- `RawContent` está limitado a `NewsSummarizer.MaxRawContentChars = 2000` caracteres.
- Medido con los 70 predeterminados: resumen desde RSS 53–66 caracteres, búsqueda web 72–84 caracteres.
- `SummaryLanguage` escribe `PromptRule()` en los prompts de ambos canales. Medido: antes de esa regla, 6 de 9 resúmenes de fuentes de Hong Kong/Taiwán contenían 1–3 caracteres tradicionales; después, 0 de 9.

## Puntuación de importancia

- Ambos canales piden al modelo un `importance` entre 0.00 y 1.00 **relativo a los últimos tres días**, con la instrucción de separar los valores (importante 0.80–1.00, rutinario 0.20–0.50).
- `ImportanceScoring.Read` analiza números y cadenas numéricas, recorta los valores fuera de rango y devuelve `null` para cualquier valor inválido.
- `ImportanceScoring.Balance` reequilibra un lote cuando es **plano** (`FlatSpread = 0.1`: dispersión < 0.1) o está **acumulado** (`PileUpRatio = 0.8`: ≥80 % en ≥ `HighPile = 0.85` o ≤ `LowPile = 0.15`) y el lote tiene al menos `MinCountForRebalance = 3` elementos; entonces reparte los valores por rango en `[LowBand = 0.15, HighBand = 0.95]`.
- `NewsRepository.GetPlaylist` filtra por `ImportanceThreshold` (**0.7** por defecto) **antes** de ordenar. Los elementos con `Importance` igual a `null` (caché antigua o fallo del modelo) se conservan siempre, así que una actualización nunca puede vaciar la biblioteca.
- Medido el 2026-10-08: una caché de 16 elementos se distribuyó `0.2-0.4: 4 / 0.4-0.6: 5 / 0.6-0.8: 4 / 0.8-1.0: 3`, y 6 elementos superaron el umbral de 0.7.

## Clasificación por palabra clave

Una fuente RSS de canal compartida devuelve un canal completo, así que sus elementos solo traen el nombre del canal. Dos mecanismos lo convierten en una etiqueta real:

1. `KeywordClassifier.Classify(title, content, keywords, fallback)` — la primera palabra clave configurada que aparece en el **título**, luego en el **contenido**, y si no hay ninguna, el valor de respaldo (normalmente el nombre del canal).
2. El prompt del resumen pide al modelo un `keyword` copiado literalmente de las palabras clave configuradas, y `KeywordClassifier.Match` lo valida contra esa lista blanca, de modo que nunca puede aparecer una categoría inventada. La respuesta del modelo prevalece sobre la coincidencia de texto.

Medido el 2026-10-08 con las seis palabras clave restauradas: 15 elementos en caché distribuidos en China 2 / Wuhan 3 / EE. UU. 3 / Taiwán 3 / Internacional 2 / IA 3, y la etiqueta en pantalla mostró `中国`.

**Esto es «sin coincidencia», no «una categoría perdida».** Con las palabras clave `武汉/AI` y la fuente de desplazamiento de chinanews, los 9 elementos de ese día trataban sobre el Día Nacional, un submarino nuclear japonés o acciones chinas: ni los títulos ni los cuerpos contenían ninguna de las dos palabras clave, y el modelo devolvió una palabra clave vacía, así que la etiqueta cayó al nombre del canal `中新网即时新闻`. **No** existe un filtro de «mostrar solo los elementos que coinciden con una palabra clave» (véanse las [limitaciones conocidas](../README.es.md)).

## Imágenes

Solo en modo texto + imagen. `ImageResolver` prueba cuatro niveles en orden: `og:image` → `twitter:image` → `link[rel=image_src]` → **la primera imagen dentro del cuerpo del artículo** (dentro del contenedor del cuerpo, después de un `<p>`, saltando logotipos, iconos, marcadores de posición y recursos de plantilla de más de 60 días — `StaleImageDays = 60`). Cada candidato se valida con una petición `Range: bytes=0-2047` y debe responder realmente `image/*` (`MaxConcurrency = 3`).

Medido el 2026-10-08: las páginas de artículo de chinanews y people.com.cn **no** tienen `og:image` ni `twitter:image`, y el RSS no tiene `enclosure` ni `media:*`, así que una extracción basada solo en metadatos nunca encontraría una imagen; el servidor de imágenes de chinanews `i2.chinanews.com.cn` tiene protección contra hotlinking y responde con HTML.

**Una imagen pertenece a un solo elemento**: `ImageResolver.DedupeImages(items)` conserva el elemento más reciente por hora de publicación y limpia el resto, y `ApplyImagesAsync(..., reservedImageUrls)` prohíbe reutilizar una imagen que otro elemento ya muestra. Un elemento que no consigue una imagen exclusiva se trata como «sin imagen en el cuerpo» y no muestra ninguna. Cada ronda también vuelve a deduplicar toda la caché.

En modo texto + imagen, cada ronda además rellena imágenes para los elementos en caché que aún no tienen, **como máximo 8 por ronda** (`MaxImageBackfill`).

Medido: antes del arreglo, 9/9 elementos tenían imagen pero con **3 grupos duplicados** (5 / 4 / 4 elementos compartiendo una misma imagen de plantilla); después del arreglo, **5/9 elementos tenían imagen y 0 grupos duplicados**.

## Retención y respaldo con lista vacía

- `RetentionWindow` agrupa por **día natural**: `RetentionDays = 3` (por defecto) conserva hoy más los dos días anteriores, y la ventana empieza en la medianoche local de anteayer (`TimeZoneInfo.Local`). Por eso la mañana y la noche ven los mismos días naturales, a diferencia de una ventana móvil `now − N×24h`.
- La ruta de obtención pasa `RetentionWindow.RetentionSpan(now, config.RetentionDays)` a `PruneExpired`. Los elementos caducados se eliminan **junto con su registro de deduplicación de URL**, así que el mismo artículo puede volver a obtenerse más adelante.
- Si el umbral de importancia elimina todos los elementos en caché, `GetPlaylist` añade un elemento aleatorio de la caché como **copia** (la caché no se modifica) y añade `NewsRepository.BelowThresholdSuffix` (`" ~"`) al final de su resumen.
- Medido: con el umbral subido temporalmente a 0.95 el panel mostró `…移送检察机关审查起诉。 ~`.

## Dónde está el código

| Aspecto | Archivo |
|---|---|
| Orquestación de la ronda y límites de relleno | `src/NewsBoard.Shared/Services/NewsFetchCoordinator.cs` |
| Fuente RSS de canal | `src/NewsBoard.Shared/Services/RssNewsSource.cs` |
| Canal de búsqueda web | `src/NewsBoard.Shared/Services/WebSearchNewsSource.cs` |
| Resumen con modelo | `src/NewsBoard.Shared/Services/NewsSummarizer.cs` |
| Longitud e idioma del resumen | `src/NewsBoard.Shared/Services/SummaryText.cs`, `src/NewsBoard.Shared/Services/AppConfig.cs` |
| Importancia | `src/NewsBoard.Shared/Services/ImportanceScoring.cs` |
| Cuerpo del artículo | `src/NewsBoard.Shared/Services/ArticleContentFetcher.cs` |
| Imágenes | `src/NewsBoard.Shared/Services/ImageResolver.cs` |
| Caché, lista de reproducción, retención | `src/NewsBoard.Shared/Services/NewsRepository.cs`, `src/NewsBoard.Shared/Services/RetentionWindow.cs` |
| Etiqueta de palabra clave | `src/NewsBoard.Shared/Services/KeywordClassifier.cs` |
