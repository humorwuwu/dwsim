# 开发与构建

## 环境要求

| 项 | 要求 |
|---|---|
| 构建 | Windows x64 + Visual Studio 2022 Build Tools（MSBuild、.NET Framework 4.6.2 targeting pack）+ NuGet |
| CI | GitHub Actions `windows-latest`（`.github/workflows/build-windows.yml`），push 触发 |
| 运行（macOS） | Apple Silicon + Rosetta 2（已安装）；Gate 0 V-3 失败，桌面窗口仍起不来，见 `docs/gate0-report.md` |
| 本机 macOS | Mono 6.12 Framework MDK（`mono` / `msbuild` / `nuget`），可本地编译桌面程序和动力学测试；完整 `DWSIM.sln` 因 WPF 工程不可构建 |

## 构建步骤

Windows 本地：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

手动等价命令：

```powershell
.\.nuget\NuGet.exe restore DWSIM.sln -PackagesDirectory packages
msbuild DWSIM.sln /p:Configuration=ReleaseWinMac /p:Platform=x64 /m
```

产物：`DWSIM\bin\x64\Release\DWSIM.exe`。

注意：上游 sln 没有 `Release|x64` 这个 solution 配置；`ReleaseWinMac|x64` 映射到项目 `Release|x64`，所以产物目录仍是 `bin\x64\Release`。

注意：本仓库是 `packages.config` + `HintPath`，**没有** MSBuild restore target，必须用 `nuget restore`。

## 本地 macOS（Mono）

- 工具链：Mono **6.12.0.206**（`msbuild` 16.10.1、`nuget` 6.6.2.1），x86_64，
  路径 `/Library/Frameworks/Mono.framework/Versions/Current/bin`。
- 桌面构建：

  ```sh
  bash scripts/build-macos.sh               # Release
  CONFIG=Debug bash scripts/build-macos.sh  # Debug
  ```

  `scripts/build.sh` 在 Darwin 上会自动转给 `build-macos.sh`。脚本只构建
  `DWSIM.UI.Desktop` 的依赖图（AnyCPU），**不构建完整 sln**：sln 里的
  `DWSIM.UI.Desktop.WPF`、`DWSIM.Controls.IronPythonConsoleControl` 等 WPF 工程
  在 Mono 上编不过。脚本同时跳过 `licenses.licx` 和 Windows `xcopy` PostBuildEvent。
- facade：脚本自动在 `$MONO_ROOT/lib/mono/*-api/Facades` 里找 `netstandard.dll`，
  传给 `NetStandardFacadesPath`。不要用 `-m` 并行；Mono 的 msbuild 会在
  `EnumProcesses` 处崩，必须 `/m:1`。
- 产物：`DWSIM.UI.Desktop/bin/<Config>/DWSIM.UI.Desktop.exe`。`PlatformFiles/Common`
  和 `PlatformFiles/macOS` 会复制到同目录；`ThermoCS/`、`DWSIM.Thermodynamics.ThermoC.dll`、
  `DWSIM.ProFeatures.dll` 会被移除（与 macOS 包一致）。
- 动力学测试：`bash scripts/test-macos.sh`。脚本会还原、构建
  `DWSIM.Automation.Dynamics.Tests`、把 `DWSIM/bin/<Config>` 里 `Private=False`
  的程序集补到测试目录，再运行；参数原样传给测试程序（例如
  `--generate-samples "PlatformFiles/Common/samples"`）。
- Io/Modbus 测试仍按 `docs/verification.md` 的 `DWSIM.H2sim.Io.Tests` 命令单独构建。
- **运行限制**：Gate 0 V-3 失败——`Eto.XamMac2` 需要 Xamarin.Mac 自带的 Mono 运行时，
  系统 Mono 6.12 没有它调用的 `_ThreadPoolWaitCallback.SetDispatcher`。编译工具链已补全，
  但桌面窗口仍不能启动；详见 `docs/verification.md`。

## 打包（Windows 单文件）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-windows.ps1
```

- 前置：先运行 `scripts/build.ps1`，`DistPackages\Windows\DWSIM.exe` 存在。
- 依赖 NSIS（`makensis`）；CI 的 `windows-latest` 已带，缺失时用 `choco install nsis`。
- 产物：`release/windows/H2sim-<version>-windows-x64.exe`。
- 首次运行时自解压到 `%LOCALAPPDATA%\H2sim\<version>`，然后启动 `DWSIM.exe`。

## 打包（macOS）

```sh
bash scripts/package-macos.sh
```

- 前置：先 `bash scripts/build-macos.sh`，`DWSIM.UI.Desktop/bin/Release/DWSIM.UI.Desktop.exe` 存在。
- 产物：`release/macos/H2sim-<version>-macos-x64.dmg`，内含 `H2sim.app`。
- 本机已用 0.3.1 跑通：`release/macos/H2sim-0.3.1-macos-x64.dmg`（168 MB，
  SHA-256 `bf2718a0…`）。
- 主程序是 x86_64，Apple Silicon 上由 Rosetta 运行。
- 启动器读取 `/Library/Frameworks/Mono.framework`（Mono 6.12）。没有安装时，打开 app 会提示先装 Mono。
- 包内不含 `ThermoCS/`（i386）、ThermoC 程序集/配置和 `DWSIM.ProFeatures.dll`。
- 未签名未公证。
- **这个包当前不能启动**。Gate 0 V-3 已失败：`Eto.XamMac2` 需要 Xamarin.Mac 自带的
  Mono 运行时，系统 Mono 6.12 缺少它调用的
  `System.Threading._ThreadPoolWaitCallback.SetDispatcher`。dmg 只用于验证打包流程，
  不能当发行物。详见 `docs/verification.md` 的「Gate 0 V-3」。

## macOS 说明

- macOS 桌面原计划走**全进程 Rosetta**（方案 M1）；产物按 `macos-x64` 命名。打包命令见上面的「打包（macOS）」。
- 方案 M1 已确认不可用（见上）。若继续 macOS 桌面，只能走**方案 M2（GTK3）**或迁到 .NET 8 的 `Eto.Platform.macOS`。
- 方案 M2 需要 **x86_64** GTK3。Homebrew 在 Apple Silicon 上装的是 arm64，x86_64 的 Mono 进程加载不了；要在 Rosetta 前缀（`/usr/local`）另装一套，或用 gtk-osx 的 x86_64 构建。
- `PlatformFiles/macOS/ThermoCS/*` 是 **Mach-O i386**，Apple Silicon 无法执行；官方样例不依赖 ThermoC，macOS 包不打包该目录（T-0.3.2）。
- `libipopt.dylib` 依赖 `/usr/local/gfortran/lib/libgfortran.3.dylib` 和 `libquadmath.0.dylib`；Rosetta 下需 `brew install gcc` 提供，缺失时只应提示「IPOPT 不可用」（T-0.3.3）。
- `DWSIM.ProFeatures.dll` 只被经典 WinForms UI 引用，macOS 包不含。
- 包未签名未公证，首次运行需右键打开（T-6.2）。

## 已知限制

- Linux/Wine/Raspberry 构建配置已移除；工程文件里的 `ReleaseLinux|*` 配置块仍保留，但 `LINUX` 编译符号不会再生效。
- Gate 0 的 V-1/V-2 需要在 Windows/CI 上验证；V-3 已在本机尝试并失败（XamMac2 运行时缺失），见 `docs/verification.md`。
- 动态样例回归基准已采集：`tests/dynamic/samples_baseline.csv`（容差 1e-6）。
- 详细任务与验收见 `docs/h2sim-implementation-plan.md`。