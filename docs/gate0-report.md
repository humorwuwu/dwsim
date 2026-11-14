# Gate 0 前置验证报告

- 日期：2026-11-14
- 仓库：`/Users/wcx/ongoing/H2sim`，分支 `h2sim/phase-a`，基线 `1abf72d1b`
- 本机：macOS 27.3（arm64），Rosetta 2 已安装；无 `msbuild`/`mono`/`nuget`/`dotnet`
- 构建环境：GitHub Actions `windows-latest`（`.github/workflows/build-windows.yml`）

## V-1 能否还原 NuGet 依赖

- 命令：`nuget restore DWSIM.sln -PackagesDirectory packages`
- 结果：**PASS**。还原 120 个包。
- 关键包版本确认：`Eto.Forms 2.8.3`、`Eto.Platform.XamMac2 2.8.2`、`SkiaSharp 1.68.2.1`、`NLog 5.3.4`。
- 备注：本仓库是 `packages.config` + `HintPath`，没有 MSBuild restore target；必须用 `nuget restore`，不能用 `msbuild -t:restore`。

## V-2 能否编译并运行现有版本（Windows）

- 命令：`scripts/build.ps1 -Configuration Debug|Release -Platform x64`
- 结果：**编译 PASS，GUI 启动待人工验证。**
  - Debug|x64：CI run 43595129121，成功。
  - Release|x64：CI run 43595129121，成功；产物 `DWSIM\bin\x64\Release\DWSIM.exe` 由脚本校验存在。
  - Release artifact：`h2sim-windows-x64`（约 293 MB，未压缩）。
- 事实修正：上游 sln **没有** `Release|x64` solution 配置；`ReleaseWinMac|x64` 映射到项目 `Release|x64`，产物仍在 `bin\x64\Release`。
- 未验证：启动主窗口、打开 `PlatformFiles/Common/samples/Water Electrolyzer.dwxmz`。CI 无交互桌面，需 Windows 机器手动确认。

## V-3 macOS 上 net462 + Mono 能否运行

- 结果：**BLOCKED**。本机没有 Mono，无法执行 V-2 产物。
- 已确认：Rosetta 2 已安装（`/Library/Apple/usr/libexec/oah`，`oahd` 运行中）。
- 影响：不阻塞 Windows MVP（Phase B）；阻塞 T-0.3 与 T-6.2 的 macOS 交付。需要安装 x86_64 Mono 或准备一台 Windows 机器实测后才能选定 M1/M2。

## V-4 ThermoCS 是否真的不可用

- `file PlatformFiles/macOS/ThermoCS/charact1` → `Mach-O executable i386`
- `lipo -info` → `Non-fat file ... architecture: i386`
- `PlatformFiles/macOS/ThermoCS/platform.txt` → 不存在
- 官方样例/测试中无 `ThermoC` 引用
- 结果：**PASS**。Apple Silicon 无法执行；禁用无回归风险。

## CI 环境补充

- 需要单独安装 .NET Framework **4.6.1** targeting pack（`choco install netfx-4.6.1-devpack`），否则 12 个项目报 `MSB3644`。
- 上游多个工程的 `PostBuildEvent` 假设 `DistPackages` 目录已存在，干净环境里末尾 `rd /s /q` 返回 1。仓库根的 `Directory.Build.targets` 在每个 `PostBuildEvent` 前预建目录，不改上游工程文件。

## 结论

- **Windows 构建链路成立**：V-1 通过，V-2 编译通过（Debug + Release），V-4 通过。
- **V-2 GUI 启动**和 **V-3 macOS/Mono** 仍未验证；前者需要 Windows 桌面，后者需要 x86_64 Mono。
- 可以继续 T-0.2、Phase B 等不依赖 macOS 运行结果的任务；T-0.3/T-6.2 在 V-3 结论出来前不排期。