# H2sim 项目规约

H2sim 是 DWSIM 9.0.5 的 GPLv3 派生仓库，目标改造为「电解水制氢工艺 + 控制仿真」专用平台。
基线 commit `1abf72d1b`（`v9.0.5-69-g1abf72d1b`）。

目录名保留 `H2sim`（含大写）。这是上游派生仓库的例外，不改成全小写，也不套 `src/`。工作区 `PROJECTS.md` 和根目录 `AGENTS.md` 的「项目例外」已登记。

执行改造前必读：

1. `docs/h2sim-implementation-plan.md`（可执行计划，任务卡 T-x.x）
2. `docs/h2-electrolyzer-retrofit-analysis.md`（物理模型分析）
3. `docs/h2sim-openplc-bridge-scope.md`（平台收敛与 OpenPLC 桥接范围）
4. `docs/gate0-report.md`（前置验证结果）

## 构建

发行路线：**Windows x64 + MSBuild**（GitHub Actions `windows-latest`）。
macOS 本机现在有 **Mono 6.12 Framework MDK**（`mono`/`msbuild`/`nuget`），可以本地编译
Eto 桌面程序和动力学测试；**完整 `DWSIM.sln` 在 macOS 上不能构建**，因为 sln 里有
WPF 渲染器和两个 WPF Python 控制台。桌面窗口能否启动仍受 Gate 0 V-3 限制。

- CI：`.github/workflows/build-windows.yml`，push 触发，产物 `DWSIM/bin/x64/Release/`。
- 本地（Windows）：

  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/build.ps1
  ```

  等价手动命令：

  ```powershell
  .\.nuget\NuGet.exe restore DWSIM.sln -PackagesDirectory packages
  msbuild DWSIM.sln /p:Configuration=ReleaseWinMac /p:Platform=x64 /m
  ```

  上游 sln 没有 `Release|x64` 这个 solution 配置；`ReleaseWinMac|x64` 映射到项目 `Release|x64`，产物仍在 `bin\x64\Release`。

- 本地（macOS）：

  ```sh
  bash scripts/build-macos.sh              # Release
  CONFIG=Debug bash scripts/build-macos.sh # Debug
  ```

  `scripts/build.sh` 在 Darwin 上会自动转给 `build-macos.sh`。脚本只构建
  `DWSIM.UI.Desktop` 的依赖图（AnyCPU），跳过 WPF 工程、`licenses.licx` 和
  Windows `xcopy` PostBuildEvent；`PlatformFiles/Common` 与 `PlatformFiles/macOS`
  会复制到输出目录，并排除 `ThermoCS/`、`DWSIM.Thermodynamics.ThermoC.dll`、
  `DWSIM.ProFeatures.dll`。产物：`DWSIM.UI.Desktop/bin/<Config>/DWSIM.UI.Desktop.exe`。
  动力学测试：`bash scripts/test-macos.sh`（参数会传给测试程序，例如
  `--generate-samples "PlatformFiles/Common/samples"`）。
- macOS CI：`.github/workflows/build-macos.yml`，`macos-15-intel` runner 安装
  Mono 6.12，跑 `build-macos.sh` / `package-macos.sh` / `test-macos.sh` 并上传 dmg。
  窗口仍不能启动（Gate 0 V-3）。

- 构建产物：`DWSIM\bin\x64\Release\DWSIM.exe`。
- Windows 单文件包：`powershell -ExecutionPolicy Bypass -File scripts/package-windows.ps1`（需要 NSIS `makensis`）。
  产物 `release/windows/H2sim-<version>-windows-x64.exe`，首次运行自解压到 `%LOCALAPPDATA%\H2sim\<version>` 并启动 `DWSIM.exe`。
- macOS `.dmg`：`bash scripts/package-macos.sh`，产物 `release/macos/H2sim-<version>-macos-x64.dmg`。启动器走 **mono + GTK3**（`H2SIM_MAC_RENDERER=Gtk3`、`PANGOCAIRO_BACKEND=fontconfig`），GTK3 已打进 dmg（`H2SIM_BUNDLE_GTK3=1`），目标机只需要 Mono 6.12（Apple Silicon 走 Rosetta）；XamMac2 启动器已弃用。CI 已验证隐藏 `/usr/local/lib` 后打包 app 仍存活并创建 `DWSIM` 主窗口；本机 Apple Silicon 已用 `open` 启动 0.3.2 dmg 并截图（`assets/macos-gtk3-0.3.2-*.png`）。完整菜单/绘图/计算交互仍待人工走查。详见 `docs/verification.md`。
- 目标框架：全仓 .NET Framework 4.6.2（少数 4.6.1/4.8）。**不要引入只支持 netstandard2.1 / net6.0+ 的包**。
- NuGet 还原用 `nuget restore`，不要用 `msbuild -t:restore`（本仓库是 packages.config + HintPath，没有 restore target）。

## 版本与产物

- 唯一版本来源：根目录 `VERSION`。
- 新功能升 `minor`，修复升 `patch`。
- 升版：`bash scripts/version.sh minor|patch`。它会改 `VERSION`、同步 `AssemblyInfo` 的 `AssemblyVersion`、在 `CHANGELOG.md` 插入新节。
  首次运行会把上游基线 `9.0.5.0` 一并改写为项目版本。
- 发行产物命名：
  - `release/windows/H2sim-<version>-windows-x64.exe`（单文件绿色版）
  - `release/macos/H2sim-<version>-macos-x64.dmg`（Rosetta 路线；将来出 arm64 再改）
- `VERSION`、产物文件名、`CHANGELOG.md` 的版本节三者必须一致。
- 用户可见的新功能或修复，先升版本再交付。

## 清理

```sh
bash scripts/clean.sh --dry-run   # 只列出
bash scripts/clean.sh             # 默认清理
bash scripts/clean.sh --deep      # 再删 packages/ 和 release/ 旧版本
```

- 默认删除：`build/`、`dist/`、`DistPackages/`、`target/`、`.build/`、`.tools/`、各工程 `bin/` `obj/`、`__pycache__`、`.pytest_cache`、`.DS_Store`、`*sync-conflict*`、`*.user`、`*.suo`。
- 默认保留：`release/`、`packages/`、源码、锁文件、`docs/`、`PlatformFiles/`、`fixtures/`、`packaging/`。
- 绝不删除：`.git/`、`.verysync/`、`PlatformFiles/`、`docs/`、`fixtures/`、`reference/`，以及任何索引库、密钥、证书（`*.sqlite3`、`*.keystore`、`*.pfx`、`certs/`）。不确定就停下来问。
- 一次阶段性测试通过、可运行产物已放进 `release/<platform>/` 后，立即运行默认清理。

## 目录角色

DWSIM 沿用「一个程序集一个顶层目录」的布局，不套 `src/`。

| 路径 | 是什么 | 进 git？ |
|---|---|---|
| 各程序集顶层目录（`DWSIM/`、`DWSIM.UnitOperations/` 等） | 手写源码 | 是 |
| `docs/` | 文档，全小写 | 是 |
| `scripts/` | 构建、清理、升版脚本 | 是 |
| `packaging/` | 打包规格（若将来新增） | 是 |
| `assets/` | 要提交的静态资源（若将来新增） | 是 |
| `release/<platform>/` | 唯一的可运行发行产物；平台名只用 `windows`/`macos` | 否 |
| `packages/` | NuGet 还原结果 | 否 |
| `dist/`、`DistPackages/`、`build/`、`target/`、`.build/` | 中间产物 | 否 |
| 各工程 `bin/`、`obj/` | 编译输出 | 否 |

## 红线

- **不要改 `WaterElectrolyzer.vb` 的计算逻辑**。它是 legacy，被 `ObjectType.WaterElectrolyzer` 与官方示例引用。
- 新增单元操作一律走 `IExternalUnitOperation` + `ObjectType.External`，放在新目录 `DWSIM.UnitOperations/UnitOperations/Electrolyzers/`。
- 枚举只在末尾追加，不要插在中间。
- 不要在通讯线程里调 `ISimulationObject.SetPropertyValue`。
- 不要提交 `release/`、`build/`、`bin/`、`obj/`、`packages/`、`DistPackages/`、`.vs/`。
- 不要在公开文档、提交说明、issue 里写真实厂站名、厂商投标原文。
- 不要为兼容 v3 做 OpenPLC 适配；只对 OpenPLC v4。H2sim 是 Modbus 从站、OPC UA client。
- macOS 包不打包 `ThermoCS/`（i386，Apple Silicon 不可执行）和 `DWSIM.ProFeatures.dll`。
- GPLv3：所有改造继续以 GPLv3 发布。

## 已知限制

- Gate 0 的 V-3（macOS + Mono 运行）尚未验证，见 `docs/gate0-report.md`。
- 本机 macOS 无构建工具链，构建以 Windows CI 为准。
- `PlatformFiles/macOS/ThermoCS/*` 是 i386，Apple Silicon 不可执行；无官方样例依赖。