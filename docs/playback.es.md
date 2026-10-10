# Motor de reproducción

[English](playback.md) | [中文](playback.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

`PlaybackEngine` (en `src/NewsBoard.Shared/Services/PlaybackEngine.cs`) es una máquina de estados de dos fases: reproduce la lista de la ronda dentro de una ventana SHOW, luego pasa a BLACKOUT y después empieza la ronda siguiente. Depende solo de las interfaces de `Abstractions`, y cada marca de tiempo viene del `IClock` inyectado, que es lo que permite dirigirlo con un reloj escalado en las pruebas.

## Máquina de estados

```
Ventana SHOW (ShowDurationSeconds, 60 s por defecto)
  → elemento por elemento por la lista de la ronda
  → termina la ventana → BLACKOUT (BlackoutDurationSeconds, 300 s por defecto)
  → SHOW otra vez (ronda siguiente)
```

| Ajuste | Efecto sobre el bucle |
|---|---|
| `ShowDurationSeconds` (60) | duración de la ventana de visualización de una ronda |
| `BlackoutDurationSeconds` (300) | cuánto dura el apagado (blackout) |
| `BlackoutCycleEnabled` (true) | desactivado significa que la pantalla permanece encendida; una **interrupción** manual aún apaga una vez |
| `ResumeAcrossRounds` (true) | un elemento cortado por la ventana conserva su lugar y los segundos ya reproducidos; si no, la ronda siguiente reinicia desde el principio de la lista |
| `WindowEndBehavior` | `FinishCurrentItem` (por defecto) deja que el elemento actual se lea hasta el final; `InterruptImmediately` corta la lectura en el límite de la ventana |
| `PlayOrder` | cómo se ordena la lista de la ronda (`Random` por defecto) |

La lista se reconstruye al empezar cada ronda, así que las noticias obtenidas a mitad de ronda las recoge la siguiente. Cuando la lista está vacía, el motor no espera una ventana completa: espera datos y arranca en cuanto llega un elemento.

## La lectura es el reloj del elemento

Con la lectura activada, un elemento dura exactamente lo que tarda en leerse: cambiar al elemento → permanecer en silencio `SpeechLeadSeconds` (1 s) → leer en voz alta; el elemento termina cuando termina la lectura. `EstimateSpeechSeconds` es la estimación compartida (chino: 0,22 s por carácter; otras escrituras: 0,32 s por palabra; ambas divididas por `SpeechRate`), y la página del panel la usa para regular el desplazamiento del texto que desborda, de modo que el texto acabe de desplazarse más o menos cuando acaba la lectura.

Cuando la lectura no está disponible — `SpeechEnabled` desactivado, el host informa de que el TTS no está disponible, o la hora actual cae dentro de `QuietHours` — el elemento recurre a una permanencia fija de `ItemHoldSeconds` (12 s) y el desplazamiento va a `ScrollSpeedPxPerSecond` (60). Con `TtsUnavailable = SkipItem`, un elemento que no se puede leer no consume tiempo y se salta.

Medido en Windows: `System.Speech` con `Microsoft Huihui Desktop` (zh-CN) leyó un resumen en 6135 ms y `SpeakCompleted` se disparó a tiempo; cancelar tardó 744 ms.

## Puerta del primer fotograma

`ArmFirstFrameGate()` hace que el primer elemento espere a que la interfaz informe de que el primer fotograma se ha pintado de verdad (`MarkFirstFrameRendered()`). La espera tiene un tope de 8 segundos, y la detención, el cambio manual o el fin de ventana la interrumpen.

Medido en Windows: antes de que existiera la puerta, la lectura empezaba unos 1,9 s antes que la imagen y los primeros 2,9–11,3 s eran negro sin texto; después, la imagen va 2,3–3,1 s por delante de la lectura.

## Controles y gestos

Hay cinco botones en la esquina inferior derecha de la página del panel:

| Botón | Acción |
|---|---|
| ⏮ anterior | elemento anterior, dando la vuelta a la lista |
| ⏭ siguiente | elemento siguiente, dando la vuelta a la lista |
| 🔗 abrir fuente | abre la URL del elemento actual en el navegador del sistema |
| ⏸ interrumpir | termina esta ronda de inmediato, apaga durante `BlackoutDurationSeconds` y luego empieza la ronda siguiente, incluso con `BlackoutCycleEnabled` desactivado |
| ⚙ ajustes | abre la página de Ajustes |

Durante el apagado, la barra de controles está oculta (`board-controls--hidden`) y la capa negra cubre la pantalla, así que un toque en cualquier punto cae sobre la capa y llama a `WakeAsync()`: el apagado termina de inmediato, la imagen vuelve y la ventana se recuenta desde el elemento actual. Tocar durante SHOW no hace nada: ni cambia de elemento ni acorta la ventana. Medido en Windows: brillo 58,4 → interrupción 0 → un toque 58,4.

## El apagado (blackout) cambia según la plataforma

| Host | Qué hace el apagado | Medido |
|---|---|---|
| WPF | apaga de verdad el monitor: `SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)` (2 = apagar, −1 = encender) | el apagado en sí no se ha confirmado a mano; ver Limitaciones conocidas |
| MAUI (Windows) | el mismo mensaje Win32; la protección se degrada a «la ventana de la app está en primer plano» porque este host no hace pantalla completa sin bordes | — |
| MAUI (Android) | brillo de ventana 0 más una capa totalmente negra; **la retroiluminación sigue encendida** | anulación de brillo `NaN` (sin anulación) → `0.0`, captura 300 867 B → 27 787 B, en 1,2 s; al tocar la pantalla vuelve a `NaN` y 300 867 B |

`MonitorPowerPolicy` es la protección compartida de lógica pura: el monitor solo se apaga cuando la ventana de la app es la ventana en primer plano **o** la ventana está en pantalla completa sin bordes. Si te cambias a otro programa, el panel sigue mostrando una capa negra en lugar de apagarte la pantalla. Volver a **encender** el monitor no está limitado a propósito: si no, un panel que se apagó y luego perdió el foco nunca podría recuperar la pantalla.

En Android no hay forma de apagar realmente el panel sin permisos: `PowerManager.GoToSleep()` exige un permiso de firma, y `DevicePolicyManager.LockNow()` necesita permisos de administrador de dispositivos y bloquea el dispositivo, así que habría que desbloquearlo para despertarlo. En Android, mantener la pantalla encendida lo lleva el indicador de ventana `KEEP_SCREEN_ON`, que el motor deja puesto a propósito durante el apagado: quitarlo dejaría que el `screen_off_timeout` del sistema apagara el panel de verdad.

## Prueba prolongada medida

Sesión de 20 minutos en el dispositivo Android (Xiaomi `M2011K2C`, Android 13), muestreando `dumpsys meminfo` cada 30 segundos:

| Periodo | TOTAL PSS | Nota |
|---|---|---|
| primeros 12 min | 230 → 239 MB | estable, subida pequeña |
| siguientes ~5 min | 258 → 280 MB | un salto de unos +40 MB |
| últimos 8 min | 295–302 MB, oscilando | meseta, sin tendencia |

- El PID nunca cambió: ni reinicio ni `PID=GONE`.
- Ninguna línea `FATAL` ni `ANR` de `com.newsboard.app` en `logcat`.
- La línea de tiempo de AudioTrack en `dumpsys audio` siguió alternando `started → stopped`; con la lectura activada, las lecturas individuales midieron 7,4 / 13,1 / 10,6 / 13,7 / 14,6 segundos, con unos 1 segundo entre ellas (= `SpeechLeadSeconds`).
- El `screen_off_timeout` de ese dispositivo es 600000 ms (10 minutos); tras 20 minutos seguía en `mWakefulness=Awake` / `mScreenOnFully=true`.
- Arranque en frío en el mismo dispositivo: `am start` → a los +2,8 s todavía el aviso de arranque (captura de 39 KB), a los +5,2 s el panel ya estaba dibujado (captura de 306 KB).

El objetivo de aceptación del plan para este punto era una prueba de 2 horas; lo que se ha medido realmente son 20 minutos.

## Limitaciones conocidas

- **El apagado en Android no es apagar el monitor**: la retroiluminación sigue encendida. La tabla de arriba dice qué hace cada host en realidad.
- **El apagado real en Windows no se ha confirmado a mano**: la implementación y su protección están puestas, pero ninguna ejecución automática ha observado que la retroiluminación cambie.
- **La prueba prolongada es de 20 minutos, no de las 2 horas que pide el plan**: la memoria llegó a una meseta, pero el comportamiento a escala de horas no está medido.
- **La calidad de la voz depende del motor que ofrezca el host**: Android usa el motor del dispositivo (medido en un Xiaomi: `com.xiaomi.mibrain.speech`) y Windows usa `System.Speech` con `Microsoft Huihui Desktop` para zh-CN. La app solo entrega texto al motor; no evalúa cómo suena.
- **Cuando el umbral de importancia deja la caché vacía**, el único elemento que se muestra es un respaldo: su resumen termina con ` ~`.
