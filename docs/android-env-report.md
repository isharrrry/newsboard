# Android 构建环境准备报告（SA5）

日期：2026-10-08　工作目录：`D:\AllData\AIDATA\NewsBoard\.wt\sa5`（分支 `sa5`）

## 一、结论

**这台机器现在可以构建 `net10.0-android`（含 MAUI Blazor Hybrid 应用）。**

硬证据：本目录下 `.tmp\probe`（`dotnet new maui-blazor` 探针，`.tmp/` 已 gitignore，不进提交）端到端构建通过，

```
.tmp\probe\bin\Debug\net10.0-android\com.companyname.probe-Signed.apk   15.30 MB
.tmp\probe\bin\Debug\net10.0-android\com.companyname.probe.apk          15.19 MB
已成功生成。 0 个警告 0 个错误     （用时 00:01:57.64）
```

**前置条件：构建前必须点源 `scripts\android-env.ps1`**（或显式传 `-p:AndroidSdkDirectory=... -p:JavaSdkDirectory=...`）。不设的话直接失败，见 §3.6 反证。

未做提权、未改机器级或用户级环境变量、未动 `C:\Program Files\dotnet` 里已装的 SDK，全部改动只落在 `D:\Android` 与 .NET 的全局 workload 目录。任务书里的「用户级 SDK 方案（`D:\dotnet` + `dotnet-install.ps1`）」**未启用**——第 1 步在系统 SDK 上就成功了，没必要再建一套（`D:\dotnet` 至今不存在）。

Release/AOT 需要的 NDK 也一并装好了（`D:\Android\Sdk\ndk\27.2.12479018`，见 §3.8）。

## 二、起点（改动前实测）

| 项 | 值 |
|---|---|
| .NET SDK | `10.0.203`（`C:\Program Files\dotnet`），runtime `10.0.7` |
| workload | **无**（`dotnet workload list` 表为空，清单版本 `10.0.200-manifests.f8ca1cb9`） |
| JDK | **无**，`JAVA_HOME` 为空 |
| Android SDK | **无**，`ANDROID_HOME` 为空 |
| PowerShell 执行策略 | `Restricted`（不能直接点源 `.ps1`，见 §五.4） |

## 三、实际执行过程（真实命令 + 真实结果）

### 3.1 `dotnet workload install maui-android` —— 直接成功，无需提权

```powershell
dotnet workload install maui-android     # 退出码 0
```

关键输出：

```
正在安装工作负载版本 10.0.204.1。
计算机有挂起的重新启动。安装将继续，但可能需要重新启动。
...
已成功安装的工作负载 maui-android。
```

下载并安装的组件共 40+ 个 MSI，包含 `Microsoft.Android.Sdk.Windows` 36.1.53（同时装了 35.0.105）、
`Microsoft.Android.Ref.36`、各 RID 的 `Microsoft.Android.Runtime.{Mono,CoreCLR,NativeAOT}.36`、
`Microsoft.Maui.*` 10.0.20、`Microsoft.Maui.Templates.net10`、Mono/AOT 交叉运行包等。

> 说明：本次 pwsh 进程本身具备写入 `C:\Program Files\dotnet` 的权限，因此**没有触发 UAC，也没走 `Start-Process -Verb RunAs`**。
> 「计算机有挂起的重新启动」是既有状态（来自更早的系统更新），不影响本任务，未处理。

验证：

```
$ dotnet workload list
工作负载版本: 10.0.204.1
已安装的工作负载 ID       清单版本                  安装源文件
maui-android      10.0.20/10.0.100      SDK 10.0.200
```

### 3.2 用户级 SDK 方案（`D:\dotnet`）—— **未启用**

第 1 步已在系统 SDK 上成功，故未下载 `dotnet-install.ps1`，未创建 `D:\dotnet`。
`scripts\android-env.ps1` 保留了对 `D:\dotnet\dotnet.exe` 的自动探测（存在则优先），属预留分支。

### 3.3 JDK 17

```powershell
Invoke-WebRequest -Uri 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' -OutFile 'D:\Android\jdk17.zip'
# OK size=186915383   elapsed=00:02:35
Expand-Archive -LiteralPath D:\Android\jdk17.zip -DestinationPath D:\Android\jdk17-extract
# 内层目录 jdk-17.0.20.1+1 整体移动到 D:\Android\jdk-17
```

验证：

```
$ D:\Android\jdk-17\bin\java.exe -version
openjdk version "17.0.20.1" 2026-08-18 LTS
OpenJDK Runtime Environment Microsoft-14940689 (build 17.0.20.1+1-LTS)
OpenJDK 64-Bit Server VM Microsoft-14940689 (build 17.0.20.1+1-LTS, mixed mode, sharing)
```

`.NET Android` 侧也认到了它：`Found Java SDK version 17.0.20.1.`

### 3.4 探针项目

```powershell
dotnet new maui-blazor -n Probe -o .tmp\probe
# 已成功创建模板“.NET MAUI Blazor Hybrid 应用”。
```

两点实测细节：

1. 该模板**不接受 `--no-restore`**（`错误: 无效选项: --no-restore`，退出码 127）；不加即可，restore 会正常跑完。
2. 模板生成的 `Probe.csproj` 默认 `TargetFrameworks` 含 `net10.0-ios;net10.0-maccatalyst;net10.0-windows...`，本机没装这些 workload，
   已把 `.tmp\probe\Probe.csproj` 收窄为单 TFM `<TargetFrameworks>net10.0-android</TargetFrameworks>`（只改探针，不动 `src\`）。

### 3.5 Android SDK：`InstallAndroidDependencies` 目标**可用**

```powershell
dotnet build -t:InstallAndroidDependencies -f net10.0-android `
  -p:AndroidSdkDirectory=D:\Android\Sdk -p:JavaSdkDirectory=D:\Android\jdk-17 `
  -p:AcceptAndroidSDKLicenses=true .tmp\probe\Probe.csproj     # 退出码 0
```

目标确实存在且工作正常（**不需要**退回到手工 `sdkmanager`）。关键输出：

```
InstallAndroidDependencies:
  Requested Java SDK with major version '17' (or greater) is already installed at 'D:\Android\jdk-17'.
  Downloading archive from 'https://dl.google.com/android/repository/platform-36_r02.zip'
  Downloading archive from 'https://dl.google.com/android/repository/build-tools_r36_windows.zip'
  Downloading archive from 'https://dl.google.com/android/repository/platform-tools_r36.0.0-win.zip'
  Downloading archive from 'https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip'
已成功生成。    2 个警告    0 个错误    已用时间 00:01:13.05
```

`AcceptAndroidSDKLicenses=true` 生效——Google SDK 许可文本被自动写入 `D:\Android\Sdk\licenses`，全程无交互提示。

安装结果：

```
D:\Android\Sdk\
  build-tools\36.0.0        platforms\android-36
  platform-tools\           cmdline-tools\latest\
  licenses\                 .knownPackages
```

顶部两条 `XA5300 找不到 Android SDK 目录` 与 `XARAT7001 NullReferenceException` 是**该阶段的正常噪音**：安装前 SDK 目录尚不存在，
`ResolveSdks` 被 `ContinueOnError=true` 降级为警告，随后由 `InstallAndroidDependencies` 把目录建出来。

### 3.6 端到端构建 + 反向对照

设置好 `AndroidSdkDirectory` 后构建：

```powershell
dotnet build .tmp\probe\Probe.csproj -f net10.0-android -c Debug `
  -p:AndroidSdkDirectory=D:\Android\Sdk -p:JavaSdkDirectory=D:\Android\jdk-17
# 已成功生成。 0 个警告 0 个错误   已用时间 00:01:57.64   → 产出 15.30 MB 已签名 APK
```

**反证（证明环境变量/脚本是必需的，也证明脚本确实生效）**：不点源脚本、不传 `-p:AndroidSdkDirectory` 时，

```powershell
dotnet build .tmp\probe\Probe.csproj -f net10.0-android -t:_ResolveSdks
# error XA5300: 找不到 Android SDK 目录。... 退出码 1
```

只点源 `scripts\android-env.ps1`、**不传任何 `-p:`** 再跑 `-t:Rebuild`：

```powershell
. .\scripts\android-env.ps1
dotnet build .tmp\probe\Probe.csproj -f net10.0-android -c Debug -t:Rebuild
# 已成功生成。 0 个警告 0 个错误   已用时间 00:03:09.75
```

（`AndroidSdkDirectory` 作为环境变量会被 MSBuild 直接当属性读入，所以脚本里的 `$env:AndroidSdkDirectory` / `$env:JavaSdkDirectory` 足够。）

### 3.7 脚本自身的验证

```
$ Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
Android 构建环境已就绪：
  DOTNET_ROOT        = C:\Program Files\dotnet
  JAVA_HOME          = D:\Android\jdk-17
  ANDROID_HOME       = D:\Android\Sdk
  AndroidSdkDirectory= D:\Android\Sdk
  ANDROID_NDK_HOME   = D:\Android\Sdk\ndk\27.2.12479018
$ . .\scripts\android-env.ps1      # 第二次点源：PATH 无重复（已按大小写不敏感 + 去尾反斜杠去重）
$ adb version
Android Debug Bridge version 1.0.41 / Version 36.0.0-13206524
```

### 3.8 NDK 27.2.12479018（Release / AOT 用）

`InstallAndroidDependencies` 不装 NDK，故用 SDK 自带的 `sdkmanager` 补装（版本取自 `.NET Android 36.1.53` 的 `Xamarin.Installer.Common.props`）：

```powershell
$env:JAVA_HOME='D:\Android\jdk-17'
cmd /c "echo y|`"D:\Android\Sdk\cmdline-tools\latest\bin\sdkmanager.bat`" --sdk_root=D:\Android\Sdk `"ndk;27.2.12479018`""
# 100% Unzipping... android-ndk-r27c/wra ... 退出码 0
```

结果：

```
D:\Android\Sdk\ndk\27.2.12479018\source.properties
  Pkg.Desc = Android NDK / Pkg.Revision = 27.2.12479018 / Pkg.ReleaseName = r27c
  toolchains\llvm\prebuilt\windows-x86_64\bin\clang.exe  存在
体积 2.20 GB
```

一条无害噪音：`Warning: Observed package id 'cmdline-tools;19.0' in inconsistent location 'D:\Android\Sdk\cmdline-tools\latest'`。
这是 .NET 与 Google 对 `cmdline-tools` 目录命名习惯不同（`latest` vs `19.0`）导致的，只影响 `sdkmanager --list` 的显示，不影响构建。

> 未实测项：本次**没有**跑 Release/AOT 发布（`RunAOTCompilation=true` 的 AOT 编译耗时很长，且属 plan §7.4 后续步骤）。
> NDK 的可用性只验证到「安装完整、clang 存在、被 .NET Android 的默认探测路径覆盖」，未验证「AOT 发布成功」。

## 四、装了什么、装在哪、占多大

| 内容 | 位置 | 体积 |
|---|---|---|
| `maui-android` workload（Android SDK 包、MAUI 包、Mono/AOT 运行时包） | `C:\Program Files\dotnet\packs\` 等（全局） | `packs` 下相关包合计 **≈2.57 GB** |
| Android SDK（platform-tools / platforms;android-36 / build-tools;36.0.0 / cmdline-tools;latest / licenses） | `D:\Android\Sdk`（不含 ndk） | **0.40 GB** |
| NDK r27c | `D:\Android\Sdk\ndk\27.2.12479018` | **2.20 GB** |
| Microsoft OpenJDK 17.0.20.1 | `D:\Android\jdk-17` | **0.29 GB** |
| 合计新增 | | **≈5.5 GB**（`D:\Android` 3.29 GB + workload 2.57 GB 中的 Android/Maui 部分） |

`packs` 主要项：`Microsoft.Android.Sdk.Windows` 460.6 MB、`Microsoft.Android.Ref.36` 203.3 MB、
四个 `Microsoft.NETCore.App.Runtime.AOT.win-x64.Cross.android-*` 各 ≈193 MB、四个 `Microsoft.NETCore.App.Runtime.Mono.android-*` 各 121–169 MB。

磁盘剩余：C 131.8→**126.5 GB**，D 205.3→**197.3 GB**（`D:\Android` 占 2.89 GB，其余为 worktree 构建产物）。

## 五、未完成项 / 已知限制

1. **NDK 已装（§3.8），但未跑过 Release/AOT 发布**。plan §7.4 的体积优化配置（`RunAOTCompilation` / `AndroidStripILAfterAOT`）目前只有「NDK 就位」这一层保证，没有端到端证据。
2. **未装 emulator、未创建 AVD**。plan §7.3 的真机调试走 USB（`dotnet build -t:Run -f net10.0-android`），不需要模拟器。
3. **只装了 `platforms;android-36`**（MAUI 模板默认目标）。若宿主项目把 `TargetFramework` 调成 `net10.0-android35.0` 之类，需要补装对应 platform。
4. **执行策略**：本机为 `Restricted`，点源脚本前需 `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force`（只影响当前进程，不改机器策略）。
5. **`src\NewsBoard.Maui` 未由本任务验证**（属另一子代理）。本报告只证明「工具链可用」，不证明该项目的 csproj 正确。
6. `sdkmanager --list` 会打印 `cmdline-tools;19.0 in inconsistent location` 警告（§3.8），可忽略；若介意，可把 `D:\Android\Sdk\cmdline-tools\latest` 复制一份为 `19.0`，但没必要。

## 六、后续构建命令模板

```powershell
# 0) 每个新终端先做一次（进程级，幂等）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
. .\scripts\android-env.ps1

# 1) Android 宿主 Debug 构建（出 APK）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -c Debug

# 2) 直接装到已连接的真机（USB 调试已开、adb devices 能看到设备）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Run

# 3) Release 打包（体积优化，plan §7.4；NDK 已就位）
dotnet publish src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -c Release
```

APK 产物位置：`src\NewsBoard.Maui\bin\<配置>\net10.0-android\*-Signed.apk`。

## 七、环境脚本

`scripts\android-env.ps1`：设置 `DOTNET_ROOT`、`JAVA_HOME`、`ANDROID_HOME` / `ANDROID_SDK_ROOT` / `AndroidSdkDirectory`、
`JavaSdkDirectory`、`ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT`（自动取 `Sdk\ndk` 下版本号最大的一个），
并把 `dotnet`、`jdk-17\bin`、`platform-tools`、`cmdline-tools\latest\bin`、`emulator` 前置到 `PATH`。
幂等、只作用于当前进程、路径不存在时只告警不中断。文件为 UTF-8 with BOM（本机默认编码为 GBK 时中文注释才不会乱码——
实测无 BOM 的 `.ps1` 点源后中文全是「鏋勫缓鐜」这类乱码，故必须保留 BOM）。
