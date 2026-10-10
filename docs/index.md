# NewsBoard documentation

**English** | [中文](index.zh-CN.md) | [Español](index.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

## Documents

| Document | English | 中文 | Español | What it covers |
|---|---|---|---|---|
| Architecture | [architecture.md](architecture.md) | [architecture.zh-CN.md](architecture.zh-CN.md) | [architecture.es.md](architecture.es.md) | The three projects and the dependency direction, the real source tree, one start → fetch → playback flow, how platform capabilities are injected, and the frozen contracts |
| Configuration | [configuration.md](configuration.md) | [configuration.zh-CN.md](configuration.zh-CN.md) | [configuration.es.md](configuration.es.md) | Where config, cache and API key live, the five Settings groups, the full configuration list (field / default / meaning), and the semantics of the easy-to-get-wrong options |
| News pipeline | [news-pipeline.md](news-pipeline.md) | [news-pipeline.zh-CN.md](news-pipeline.zh-CN.md) | [news-pipeline.es.md](news-pipeline.es.md) | The two fetch channels and the `Auto` fallback order, URL dedup and AI cooldown, article-body fetch, summarization and importance scoring, keyword classification, image selection, the retention window and the empty-playlist fallback |
| Playback | [playback.md](playback.md) | [playback.zh-CN.md](playback.zh-CN.md) | [playback.es.md](playback.es.md) | The SHOW → BLACKOUT → SHOW state machine, TTS as the per-item clock, the first-frame gate, the five control buttons and gestures, the platform difference of blackout, and the 20-minute soak result |
| Android | [android.md](android.md) | [android.zh-CN.md](android.zh-CN.md) | [android.es.md](android.es.md) | Toolchain prerequisites, build and deploy commands, deployment on a real device, the three platform facts, and a link to the environment report |
| Troubleshooting | [troubleshooting.md](troubleshooting.md) | [troubleshooting.zh-CN.md](troubleshooting.zh-CN.md) | [troubleshooting.es.md](troubleshooting.es.md) | Symptom → cause → fix entries, each with a runnable command or a concrete path |
| Contributing | [contributing.md](contributing.md) | [contributing.zh-CN.md](contributing.zh-CN.md) | [contributing.es.md](contributing.es.md) | Environment and commands, file ownership, frozen contracts, coding conventions, the pre-commit gate, the documentation discipline and the not-yet-verified list |
| Android environment report | [android-env-report.md](android-env-report.md) | — | — | Measured Android toolchain report (workloads / JDK / SDK / NDK and disk usage). Single language (Chinese). |

## Where to start

| I want to… | Go to |
|---|---|
| Run the board for the first time | [README](../README.md) — requirements and commands |
| Change settings | [configuration.md](configuration.md) |
| Understand where the news comes from | [news-pipeline.md](news-pipeline.md) |
| Understand how one news item is played | [playback.md](playback.md) |
| Change the code | [architecture.md](architecture.md) and [contributing.md](contributing.md) |
| Build for Android and deploy to a phone | [android.md](android.md) |
| Fix something that does not work | [troubleshooting.md](troubleshooting.md) |

## About these documents

- Every number here was measured on this machine (2026-10-08): the unit-test count, device behaviour, memory figures, timings. Nothing is estimated or rounded into "about".
- Anything that has not been verified is labelled **not verified** instead of being presented as working.
- Changing behaviour, a configuration field or a command means updating all three language versions in the same commit, together with `AGENTS.md` and `plans/news-board.plan.md`.
- `plans/news-board.plan.md` is the implementation plan: the decision records, the architecture, the configuration list, the task list with status, and the round-by-round iteration history.
