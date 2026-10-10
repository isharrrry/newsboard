# Android

[English](android.md) | [中文](android.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

El objetivo principal de NewsBoard es Android: un host MAUI (`src/NewsBoard.Maui`, framework de destino `net10.0-android`) que renderiza la misma interfaz Blazor que los hosts de Windows, en un teléfono que se queda sobre la mesa y lee las noticias en voz alta.

## Cadena de herramientas y requisitos

En esta máquina la cadena de herramientas vive en `D:\Android`, y `scripts\android-env.ps1` apunta el terminal hacia ella.

| Pieza | Ruta / versión |
|---|---|
| .NET SDK | `10.0.203`, runtime `10.0.7` |
| workload | `maui-android` `10.0.20/10.0.100` (versión del workload `10.0.204.1`) |
| JDK 17 | `D:\Android\jdk-17` (OpenJDK 17.0.20.1), 0.29 GB |
| Android SDK | `D:\Android\Sdk` — `platforms;android-36`, `build-tools;36.0.0`, `platform-tools`, `cmdline-tools;latest`, 0.40 GB |
| NDK | `D:\Android\Sdk\ndk\27.2.12479018` (r27c), 2.20 GB |

**Cada terminal nuevo debe cargar el script primero** — la política de ejecución de esta máquina es `Restricted`, así que hay que desbloquear el proceso actual antes de cargarlo:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
. .\scripts\android-env.ps1
```

El script define `DOTNET_ROOT`, `JAVA_HOME`, `ANDROID_HOME` / `ANDROID_SDK_ROOT` / `AndroidSdkDirectory`, `JavaSdkDirectory`, `ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT` y antepone `dotnet`, `jdk-17\bin`, `platform-tools`, `cmdline-tools\latest\bin` y `emulator` al `PATH`. Es idempotente, afecta solo al proceso actual y únicamente avisa cuando falta una ruta. Debe mantenerse en **UTF-8 con BOM** (sin BOM los comentarios en chino salen corruptos tras cargarlo). Si se omite, la compilación falla con `error XA5300 找不到 Android SDK 目录`.

El inventario completo del entorno — qué se instaló, dónde, cuánto ocupa y qué nunca se probó — está en [android-env-report.md](android-env-report.md).

## Compilación

```powershell
# APK de Debug -> src\NewsBoard.Maui\bin\Debug\net10.0-android\*-Signed.apk
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Release: autocontenido y distribuible
dotnet publish src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -c Release

# toda la solución (WPF + MAUI/Android + MAUI/Windows)
dotnet build NewsBoard.slnx -v q --nologo
```

## Despliegue en un dispositivo

**Una compilación de Debug no se puede instalar solo con `adb install`.** Las compilaciones de Debug usan el **Fast Deployment** de .NET Android: los ensamblados no están dentro del APK, sino que el destino de despliegue los envía aparte a `files/.__override__/<abi>` en el dispositivo. Instalar únicamente el `*-Signed.apk` provoca un fallo al arrancar:

```
F/libc: Fatal signal 6 (SIGABRT), code -1 (SI_QUEUE) in tid 12152 (m.newsboard.app)
Abort message: 'No assemblies found in '/data/user/0/com.newsboard.app/files/.__override__/arm64-v8a'
                or '<unavailable>'. Assuming this is part of Fast Deployment. Exiting...'
backtrace: #00 libc.so (abort)  ←  #01 libmonodroid.so (abort_application)
```

Hay que usar el destino de despliegue oficial (`-t:Run` también sirve y además inicia la aplicación):

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

Tras instalar, los ensamblados enviados se pueden contar con `adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l` (440 archivos en esta ejecución). Para un paquete que se pueda instalar de forma independiente, hay que compilar en Release.

## Aceptación medida en el dispositivo

Dispositivo: Xiaomi `M2011K2C` (Redmi K40), Android 13 / SDK 33 / arm64-v8a, número de serie `<device-serial>`.

| Elemento | Resultado |
|---|---|
| Instalación | `com.newsboard.app` v1.0, minSdk 24 / targetSdk 36 |
| Arranque | el proceso se mantiene vivo, sin FATAL ni abort; el primer arranque pide el permiso de notificaciones (un diálogo de MIUI) y el panel aparece tras concederlo |
| Renderizado | etiqueta de palabra clave + reloj + resumen en letra grande + barra de control inferior (⏮ ⏭ 🔗 ⏸ ⚙) |
| Obtención | **16 elementos**, las seis palabras clave acertadas: China 2 / Wuhan 3 / EE. UU. 3 / Taiwán 3 / Internacional 2 / IA 3; con resúmenes e importancia (ejemplo 0.81), canal «búsqueda web» |
| Datos | `files/NewsBoard/config.json` (1817 B) + `files/NewsBoard/news.json` (22464 B), ambos en el directorio privado de la aplicación |

Arranque en frío medido con `am start`: a los +2.8 s la pantalla aún muestra el aviso de arranque (captura de 39 KB) y a los **+5.2 s el panel ya está renderizado** (306 KB).

## Hecho de plataforma 1: el apagado no es apagar la pantalla

La implementación de `IScreenController` en Android pone `WindowManager.LayoutParams.ScreenBrightness` de la ventana a `0f` y pinta una capa completamente negra. **No** apaga la retroiluminación.

| Momento | Acción | `mScreenBrightnessOverrideFromWindowManager` | Bytes de la captura |
|---|---|---|---|
| Mostrando | — | `NaN` (sin anulación = brillo del sistema) | — |
| Pulsar «interrumpir» (4.º botón de control) | `input tap 919 2287` | **`0.0`** | **27,787** (todo negro) |
| Tocar el centro de la pantalla para despertar | `input tap 540 1200` | **`NaN`** | **300,867** (panel restaurado) |

La transición surte efecto en **1.2 s** en ambos sentidos; durante el apagado `dumpsys audio` muestra que la lectura ya está `stopped`.

**Mantener la pantalla encendida** recae en el flag de ventana: `dumpsys window` muestra `fl=… KEEP_SCREEN_ON …` y `mLastWakeLockHoldingWindow=…MainActivity`. El controlador **conserva** deliberadamente ese flag durante el apagado, así que el sistema no apaga la pantalla tras `screen_off_timeout` (600000 ms = 10 minutos en este dispositivo): la prueba de 20 minutos mantuvo `mWakefulness=Awake / mScreenOnFully=true` en todo momento.

**Limitación**: como la retroiluminación sigue encendida, el apagado no es un apagado real de pantalla. Android no ofrece nada sin permisos que apague el panel: `PowerManager.GoToSleep()` es un permiso de firma `DEVICE_POWER` y `DevicePolicyManager.LockNow()` requiere derechos de administrador de dispositivos **y bloquea la pantalla**, lo que choca con «tocar la pantalla para despertar». Esto es lo más cerca que llega una aplicación sin privilegios; un apagado real exigiría la vía del administrador de dispositivos y una decisión de producto. Windows no se ve afectado (`SC_MONITORPOWER` apaga de verdad el monitor).

## Hecho de plataforma 2: ¿está sonando de verdad? Mira `dumpsys audio`

Android usa el **motor TTS del propio dispositivo**; en esta máquina `settings get secure tts_default_synth` es `com.xiaomi.mibrain.speech` (`tts_default_locale=null`). Un enlace correcto se ve así:

```
I TextToSpeech: Sucessfully bound to com.xiaomi.mibrain.speech
I TextToSpeech: Connected to TTS engine
```

`logcat` no dice si salió sonido; la pista de audio sí. En `dumpsys audio`, `com.xiaomi.mibrain.speech` (pid 24016) crea un `AudioTrack` por lectura (`attr: usage=USAGE_MEDIA content=CONTENT_TYPE_SPEECH`), y `started→stopped` es exactamente una lectura en voz alta. Medido, cinco seguidas:

| # | started | stopped | Duración |
|---|---|---|---|
| 1 | 16:11:07.883 | 16:11:15.318 | 7.4 s |
| 2 | 16:11:17.260 | 16:11:30.354 | 13.1 s |
| 3 | 16:11:31.912 | 16:11:42.497 | 10.6 s |
| 4 | 16:11:43.996 | 16:11:57.674 | 13.7 s |
| 5 | 16:11:59.048 | 16:12:13.656 | 14.6 s |

El hueco entre elementos es de ≈1 s, que es `SpeechLeadSeconds`. Las duraciones encajan con un resumen chino de 70 caracteres a unas 5 caracteres por segundo. Eso demuestra tres cosas a la vez: la lectura ocurre de verdad, `UtteranceProgressListener.OnDone` se dispara a tiempo y el motor de reproducción usa realmente «lectura terminada» como reloj del elemento.

**La calidad de voz no se puede evaluar objetivamente** — no hay bucle de audio; solo una persona puede juzgarla. La aplicación solo entrega el texto al motor del sistema.

## Hecho de plataforma 3: MIUI bloquea el inicio automático al arrancar

El lado del código es correcto. `BootReceiver` está registrado con tres acciones (`BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `QUICKBOOT_POWERON`), con `enabled=true exported=true`; el interruptor booleano vale `true` por defecto cuando no existe archivo de preferencias, y `RECEIVE_BOOT_COMPLETED` está declarado. Tras reiniciar, la difusión **sí se entrega** al receptor:

```
Deliver 0 #154: (manifest)
ActivityInfo:
  name=crc64017365ed8da6996e.BootReceiver
  packageName=com.newsboard.app
  enabled=true exported=true directBootAware=false
```

Y a continuación el sistema se niega a arrancar el proceso (esta clase es específica de MIUI, no existe en AOSP):

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED flg=0x89000010 (has extras) }:
  process is not permitted to auto start
```

Dos hechos que lo corroboran: `cmd appops get com.newsboard.app 10021` devuelve **`MIUIOP(10021): ignore`** (10021 es la operación de inicio automático propia de MIUI), y un `QUICKBOOT_POWERON` enviado a mano cae en la cola «Historical Broadcast **background**» con `dispatchTime=--` / `dispatchClockTime=1970-01-01`, es decir, nunca se despacha, mientras el proceso nunca aparece.

**Conclusión: es la política de inicio automático de MIUI, no un defecto del código.** Para permitirlo: Ajustes → Aplicaciones → Gestionar aplicaciones → NewsBoard → **Inicio automático** (activarlo); además se recomienda poner el ahorro de batería en «Sin restricciones».

Habilitarlo desde un shell no es posible: el dispositivo no tiene root (`su` no se encuentra), `cmd appops set com.newsboard.app 10021 allow` responde `No operations.` y `am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` falla con `Error type 3 … does not exist` aunque `dumpsys package` liste esa actividad. Por eso `AutoStartHelper.OpenAutoStartSettings()` cae con elegancia en la página de detalles de la aplicación en este dispositivo; eso no es un error.

Así se vuelve a probar tras concederlo:

```powershell
adb reboot
adb wait-for-device
adb shell getprop sys.boot_completed          # esperar a que sea 1
adb shell pidof com.newsboard.app             # debe ser no vacío
adb shell dumpsys activity services com.newsboard.app | Select-String isForeground
# esperado: NewsBoardForegroundService isForeground=true foregroundId=1001
adb shell cmd appops get com.newsboard.app 10021   # debe pasar a allow
```

## Prueba de estabilidad

Durante 20 minutos desde un arranque en frío, tomando `dumpsys meminfo <pid> | grep TOTAL` cada 30 s (40 muestras), siempre el mismo PID `10223`: sin reinicios y sin `PID=GONE`:

| Periodo | TOTAL PSS | Nota |
|---|---|---|
| 16:18:58 → 16:24:30 (muestras 1–12) | 230 → 239 MB | estable los primeros 12 minutos |
| 16:25:00 → 16:29:31 (13–22) | 258 → 280 MB | un salto de ≈+40 MB (renderizado de WebView / búferes de red) |
| 16:30:02 → 16:38:34 (23–40) | **banda de 295–302 MB** | plana los últimos 8 minutos, sin tendencia al alza |

- **Fallos / ANR**: ni un solo FATAL o ANR de `newsboard` en `logcat` (las dos líneas `SIGABRT` encontradas son del proceso `init` del sistema durante el arranque).
- **La reproducción sigue avanzando**: la línea temporal de AudioTrack en `dumpsys audio` alterna `started→stopped` durante los 20 minutos; la última se detuvo a las 16:38:04, un minuto antes de terminar el muestreo.
- **La pantalla permanece encendida**: el `screen_off_timeout` del dispositivo es 600000 ms (10 minutos) y tras 20 minutos seguía en `mWakefulness=Awake / mScreenOnFully=true`.
- **Servicio en primer plano**: `NewsBoardForegroundService isForeground=true foregroundId=1001`, con notificación permanente presente.

**Aviso de alcance**: el criterio de aceptación original de estabilidad en Android era de **2 horas**; esta ejecución cubrió 20 minutos. Basta para demostrar «no se cae, no se atasca, no apaga la pantalla, la memoria se estabiliza», pero no para juzgar la estabilidad a escala de horas — en particular si al salto de +40 MB le sigue una segunda fase de crecimiento.

## Limitaciones conocidas

- La calidad de voz en Android depende del motor TTS integrado del teléfono y no se ha evaluado objetivamente.
- El inicio automático al arrancar requiere autorizarlo a mano en el gestor de inicio automático del fabricante (en este dispositivo `MIUIOP(10021)` está actualmente en `ignore`).
- La prueba de 20 minutos no alcanza el criterio de 2 horas.
- No se ha ejecutado una publicación Release/AOT; solo se ha verificado que el NDK está en su sitio (véase [android-env-report.md](android-env-report.md)).
