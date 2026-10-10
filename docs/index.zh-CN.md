# NewsBoard 文档

[English](index.md) | **中文** | [Español](index.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

## 文档

| 文档 | English | 中文 | Español | 讲什么 |
|---|---|---|---|---|
| 架构 | [architecture.md](architecture.md) | [architecture.zh-CN.md](architecture.zh-CN.md) | [architecture.es.md](architecture.es.md) | 一套组件与三个宿主、依赖方向、真实的源码目录树、一次「启动 → 抓取 → 播放」的数据流、平台能力如何注入、平台差异，以及冻结契约清单 |
| 配置 | [configuration.md](configuration.md) | [configuration.zh-CN.md](configuration.zh-CN.md) | [configuration.es.md](configuration.es.md) | 配置、缓存与 API Key 在哪，设置页的 5 个分组，完整配置项清单（字段 / 默认值 / 含义），以及几个易错项的语义 |
| 新闻流水线 | [news-pipeline.md](news-pipeline.md) | [news-pipeline.zh-CN.md](news-pipeline.zh-CN.md) | [news-pipeline.es.md](news-pipeline.es.md) | 两条抓取通道与「自动」的回退顺序、URL 去重与 AI 冷却、正文抓取、摘要与重要度打分、关键字归类、配图取舍、保留窗口、空播放列表兜底 |
| 播放 | [playback.md](playback.md) | [playback.zh-CN.md](playback.zh-CN.md) | [playback.es.md](playback.es.md) | SHOW → BLACKOUT → SHOW 状态机、TTS 即条目时钟、首帧闸门、5 个控制按钮与手势、灭屏的平台差异、20 分钟长稳结果 |
| Android | [android.md](android.md) | [android.zh-CN.md](android.zh-CN.md) | [android.es.md](android.es.md) | 平台篇之一 —— 跨平台总览见 README 与 architecture：工具链前置、构建与部署命令、真机部署、三条平台事实，以及指向环境报告的链接 |
| Windows | [windows.md](windows.md) | [windows.zh-CN.md](windows.zh-CN.md) | [windows.es.md](windows.es.md) | Windows 工具链与两个宿主的构建运行、真关屏、语音、自启，以及 WPF 与 MAUI Windows 头的差异 |
| 排错 | [troubleshooting.md](troubleshooting.md) | [troubleshooting.zh-CN.md](troubleshooting.zh-CN.md) | [troubleshooting.es.md](troubleshooting.es.md) | 症状 → 原因 → 处置，每条都给出可执行的命令或明确路径 |
| 参与开发 | [contributing.md](contributing.md) | [contributing.zh-CN.md](contributing.zh-CN.md) | [contributing.es.md](contributing.es.md) | 环境与命令、文件所有权、冻结契约、编码约定、提交前门禁、文档纪律，以及尚未验证清单 |
| Android 环境报告 | [android-env-report.md](android-env-report.md) | — | — | Android 工具链环境实测报告（workload / JDK / SDK / NDK 与占用）。单语（中文）。 |

## 从哪开始

| 我想… | 去哪 |
|---|---|
| 第一次把看板跑起来 | [README](../README.zh-CN.md) —— 前置条件与命令 |
| 改设置 | [configuration.md](configuration.md) |
| 搞清新闻是从哪来的 | [news-pipeline.md](news-pipeline.md) |
| 搞清一条新闻是怎么播的 | [playback.md](playback.md) |
| 改代码 | [architecture.md](architecture.md) 与 [contributing.md](contributing.md) |
| 在 Android 上构建并装到手机上 | [android.md](android.md) |
| 在 Windows 上构建和运行 | [windows.md](windows.md) |
| 修一个不工作的东西 | [troubleshooting.md](troubleshooting.md) |

## 关于这些文档

- 这里的每个数字都是在本机实测出来的（2026-10-08）：单元测试条数、设备行为、内存数值、时间。没有估算值，也没有四舍五入成「约」。
- 凡是没验证过的内容，一律标注为**未验证**，而不是写成「应该能用」。
- 改了行为、配置项或命令，就要在同一次提交里同步三个语言版本，并同时更新 `AGENTS.md` 与 `plans/news-board.plan.md`。
- `plans/news-board.plan.md` 是实施方案：决策记录、架构、配置清单、带完成状态的任务表，以及逐轮迭代记录。
