# NewsBoard —— Android（net10.0-android）构建环境设置
#
# 用法（点源，幂等，可在同一会话里重复执行）：
#     Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force   # 本机默认是 Restricted，仅放开当前进程
#     . .\scripts\android-env.ps1
#
#   一次性构建（不污染会话）：
#     pwsh -NoProfile -ExecutionPolicy Bypass -Command ". .\scripts\android-env.ps1; dotnet build src\NewsBoard.Maui -f net10.0-android"
#
# 说明：
#   1. 只设置【当前 PowerShell 进程】的环境变量，绝不修改机器级 / 用户级环境变量。
#   2. 本机实际安装位置：
#        .NET SDK    : C:\Program Files\dotnet   （已装 maui-android workload）
#        JDK 17      : D:\Android\jdk-17         （Microsoft OpenJDK 17）
#        Android SDK : D:\Android\Sdk            （platform-tools / platforms / build-tools）
#   3. 若某路径不存在，只发警告并把对应变量指向已有值，不抛异常中断会话。

[CmdletBinding()]
param(
    [string]$DotnetRoot = 'C:\Program Files\dotnet',
    [string]$JavaHome   = 'D:\Android\jdk-17',
    [string]$AndroidSdk = 'D:\Android\Sdk'
)

# 若 D:\dotnet 存在（用户级 SDK 方案），优先使用它
if (Test-Path -LiteralPath 'D:\dotnet\dotnet.exe') { $DotnetRoot = 'D:\dotnet' }

function Add-PathEntry {
    param([string]$Dir)
    if (-not (Test-Path -LiteralPath $Dir)) { return }
    # 忽略大小写与结尾反斜杠，避免重复追加
    $norm = $Dir.TrimEnd('\')
    $hit = $env:PATH -split ';' | Where-Object { $_.TrimEnd('\') -ieq $norm } | Select-Object -First 1
    if (-not $hit) {
        $env:PATH = ($Dir + ';' + $env:PATH)
    }
}

# ---- .NET ----
if (Test-Path -LiteralPath (Join-Path $DotnetRoot 'dotnet.exe')) {
    $env:DOTNET_ROOT = $DotnetRoot
    Add-PathEntry $DotnetRoot
} else {
    Write-Warning "找不到 dotnet.exe：$DotnetRoot（DOTNET_ROOT 未设置）"
}
# 关闭首次运行欢迎信息，避免污染构建输出
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# ---- JDK ----
if (Test-Path -LiteralPath (Join-Path $JavaHome 'bin\java.exe')) {
    $env:JAVA_HOME = $JavaHome
    Add-PathEntry (Join-Path $JavaHome 'bin')
} else {
    Write-Warning "找不到 java.exe：$JavaHome\bin（JAVA_HOME 未设置）"
}

# ---- Android SDK ----
if (Test-Path -LiteralPath $AndroidSdk) {
    $env:ANDROID_HOME = $AndroidSdk
    $env:ANDROID_SDK_ROOT = $AndroidSdk
    $env:AndroidSdkDirectory = $AndroidSdk       # MSBuild 属性同名变量，便于直接 dotnet build
    Add-PathEntry (Join-Path $AndroidSdk 'platform-tools')
    Add-PathEntry (Join-Path $AndroidSdk 'emulator')
    Add-PathEntry (Join-Path $AndroidSdk 'cmdline-tools\latest\bin')
} else {
    Write-Warning "找不到 Android SDK：$AndroidSdk（ANDROID_HOME 未设置）"
}

# ---- NDK（Release / AOT 编译需要；.NET Android 会在 <Sdk>\ndk\<版本> 下自动查找）----
if ($env:ANDROID_HOME) {
    $ndkRoot = Join-Path $env:ANDROID_HOME 'ndk'
    if (Test-Path -LiteralPath $ndkRoot) {
        $ndk = Get-ChildItem -LiteralPath $ndkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($ndk) {
            $env:ANDROID_NDK_HOME = $ndk.FullName
            $env:ANDROID_NDK_ROOT = $ndk.FullName
        }
    }
}

# ---- MSBuild 侧参数（供 dotnet build 直接读取）----
if ($env:JAVA_HOME) { $env:JavaSdkDirectory = $env:JAVA_HOME }

Write-Host "Android 构建环境已就绪：" -ForegroundColor Green
Write-Host ("  DOTNET_ROOT        = {0}" -f $env:DOTNET_ROOT)
Write-Host ("  JAVA_HOME          = {0}" -f $env:JAVA_HOME)
Write-Host ("  ANDROID_HOME       = {0}" -f $env:ANDROID_HOME)
Write-Host ("  AndroidSdkDirectory= {0}" -f $env:AndroidSdkDirectory)
Write-Host ("  ANDROID_NDK_HOME   = {0}" -f $env:ANDROID_NDK_HOME)
