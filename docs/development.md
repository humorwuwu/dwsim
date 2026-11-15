# 开发与构建

## 环境要求

| 项 | 要求 |
|---|---|
| 构建 | Windows x64 + Visual Studio 2022 Build Tools（MSBuild、.NET Framework 4.6.2 targeting pack）+ NuGet |
| CI | GitHub Actions `windows-latest`（`.github/workflows/build-windows.yml`），push 触发 |
| 运行（macOS） | Apple Silicon + Rosetta 2（已安装）；Gate 0 V-3 尚未验证，见 `docs/gate0-report.md` |
| 本机 macOS | **没有** `dotnet` / `mono` / `msbuild` / `nuget`，不能本地构建 |

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

- 安装：官方 Mono 6.12（x86_64，Rosetta 下运行），路径 `/Library/Frameworks/Mono.framework/Versions/Current/bin`。
- 还原：`nuget restore DWSIM.sln -PackagesDirectory packages`。
- 构建 Io 测试：

  ```sh
  msbuild DWSIM.H2sim.Io.Tests/DWSIM.H2sim.Io.Tests.csproj \
    /p:Configuration=Release /p:Platform=AnyCPU \
    /p:PostBuildEvent= /p:GenerateSerializationAssemblies=Off \
    /p:NetStandardFacadesPath=/Library/Frameworks/Mono.framework/Versions/6.12.0/lib/mono/4.7.2-api/Facades
  ```

- `Directory.Build.targets` 会在非 Windows 上归一化 HintPath 并补 netstandard facade。
- 限制：`DWSIM.Controls.PythonConsoleControl` 是 WPF 工程，Mono 无法编译，完整 Dynamics/Automation 构建需要 CI 产物或 Windows。

## 打包（Windows 单文件）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-windows.ps1
```

- 前置：先运行 `scripts/build.ps1`，`DistPackages\Windows\DWSIM.exe` 存在。
- 依赖 NSIS（`makensis`）；CI 的 `windows-latest` 已带，缺失时用 `choco install nsis`。
- 产物：`release/windows/H2sim-<version>-windows-x64.exe`。
- 首次运行时自解压到 `%LOCALAPPDATA%\H2sim\<version>`，然后启动 `DWSIM.exe`。
- macOS `.dmg` 暂不产出：Gate 0 V-3（x86_64 Mono）尚未验证。

## macOS 说明

- macOS 版计划走**全进程 Rosetta**（方案 M1）；产物按 `macos-x64` 命名。
- `PlatformFiles/macOS/ThermoCS/*` 是 **Mach-O i386**，Apple Silicon 无法执行；官方样例不依赖 ThermoC，macOS 包不打包该目录（T-0.3.2）。
- `libipopt.dylib` 依赖 `/usr/local/gfortran/lib/libgfortran.3.dylib` 和 `libquadmath.0.dylib`；Rosetta 下需 `brew install gcc` 提供，缺失时只应提示「IPOPT 不可用」（T-0.3.3）。
- `DWSIM.ProFeatures.dll` 只被经典 WinForms UI 引用，macOS 包不含。
- 包未签名未公证，首次运行需右键打开（T-6.2）。

## 已知限制

- Linux/Wine/Raspberry 构建配置已移除；工程文件里的 `ReleaseLinux|*` 配置块仍保留，但 `LINUX` 编译符号不会再生效。
- Gate 0 的 V-1/V-2 需要在 Windows/CI 上验证；V-3 需要 Mono 才能在本机验证。
- 动态样例回归基准（T-3.1 前置）尚未采集。
- 详细任务与验收见 `docs/h2sim-implementation-plan.md`。