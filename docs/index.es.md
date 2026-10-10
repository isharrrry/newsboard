# Documentación de NewsBoard

[English](index.md) | [中文](index.zh-CN.md) | **Español**

> Parte de la documentación de NewsBoard — volver al [README](../README.es.md)

## Documentos

| Documento | English | 中文 | Español | Qué cubre |
|---|---|---|---|---|
| Arquitectura | [architecture.md](architecture.md) | [architecture.zh-CN.md](architecture.zh-CN.md) | [architecture.es.md](architecture.es.md) | Un conjunto de componentes y los tres hosts, la dirección de dependencias, el árbol de código real, un flujo inicio → obtención → reproducción, cómo se inyectan las capacidades de plataforma, las diferencias entre plataformas y los contratos congelados |
| Configuración | [configuration.md](configuration.md) | [configuration.zh-CN.md](configuration.zh-CN.md) | [configuration.es.md](configuration.es.md) | Dónde viven la configuración, la caché y la clave de API, los cinco grupos de la página de Ajustes, la lista completa de opciones (campo / valor por defecto / significado) y la semántica de las opciones que se malinterpretan con facilidad |
| Flujo de noticias | [news-pipeline.md](news-pipeline.md) | [news-pipeline.zh-CN.md](news-pipeline.zh-CN.md) | [news-pipeline.es.md](news-pipeline.es.md) | Los dos canales de obtención y el orden de respaldo de «Auto», la deduplicación por URL y el enfriamiento de la IA, la obtención del cuerpo del artículo, el resumen y la puntuación de importancia, la clasificación por palabra clave, la selección de imagen, la ventana de retención y el respaldo cuando la lista de reproducción queda vacía |
| Reproducción | [playback.md](playback.md) | [playback.zh-CN.md](playback.zh-CN.md) | [playback.es.md](playback.es.md) | La máquina de estados SHOW → BLACKOUT → SHOW, la lectura en voz alta como reloj del elemento, la compuerta del primer fotograma, los cinco botones y gestos, la diferencia de plataforma en el apagado (blackout) y el resultado de 20 minutos de estabilidad |
| Android | [android.md](android.md) | [android.zh-CN.md](android.zh-CN.md) | [android.es.md](android.es.md) | Uno de los capítulos de plataforma — el panorama multiplataforma está en el README y en architecture: requisitos de la cadena de herramientas, comandos de compilación y despliegue, despliegue en un dispositivo real, los tres hechos de plataforma y un enlace al informe de entorno |
| Windows | [windows.md](windows.md) | [windows.zh-CN.md](windows.zh-CN.md) | [windows.es.md](windows.es.md) | La cadena de herramientas de Windows, la compilación y ejecución de los dos hosts, el apagado real del monitor, la voz, el inicio automático y las diferencias entre WPF y la cabecera Windows de MAUI |
| Solución de problemas | [troubleshooting.md](troubleshooting.md) | [troubleshooting.zh-CN.md](troubleshooting.zh-CN.md) | [troubleshooting.es.md](troubleshooting.es.md) | Entradas síntoma → causa → solución, cada una con un comando ejecutable o una ruta concreta |
| Contribuir | [contributing.md](contributing.md) | [contributing.zh-CN.md](contributing.zh-CN.md) | [contributing.es.md](contributing.es.md) | Entorno y comandos, propiedad de los archivos, contratos congelados, convenciones de código, la compuerta previa al commit, la disciplina de documentación y la lista de lo aún no verificado |
| Informe de entorno de Android | [android-env-report.md](android-env-report.md) | — | — | Informe medido de la cadena de herramientas de Android (workloads / JDK / SDK / NDK y uso de disco). Un solo idioma (chino). |

## Por dónde empezar

| Quiero… | Ir a |
|---|---|
| Ejecutar el panel por primera vez | [README](../README.es.md) — requisitos y comandos |
| Cambiar los ajustes | [configuration.md](configuration.md) |
| Entender de dónde vienen las noticias | [news-pipeline.md](news-pipeline.md) |
| Entender cómo se reproduce una noticia | [playback.md](playback.md) |
| Cambiar el código | [architecture.md](architecture.md) y [contributing.md](contributing.md) |
| Compilar e instalar en Android | [android.md](android.md) |
| Compilar y ejecutar en Windows | [windows.md](windows.md) |
| Arreglar algo que no funciona | [troubleshooting.md](troubleshooting.md) |

## Sobre esta documentación

- Cada número que aparece aquí se midió en esta máquina (2026-10-08): el número de pruebas unitarias, el comportamiento en el dispositivo, las cifras de memoria, los tiempos. Nada está estimado ni redondeado a «aproximadamente».
- Todo lo que no se ha verificado se etiqueta como **no verificado** en lugar de presentarse como si funcionara.
- Cambiar un comportamiento, un campo de configuración o un comando implica actualizar las tres versiones de idioma en el mismo commit, junto con `AGENTS.md` y `plans/news-board.plan.md`.
- `plans/news-board.plan.md` es el plan de implementación: los registros de decisiones, la arquitectura, la lista de configuración, la lista de tareas con su estado y el historial de iteraciones.
