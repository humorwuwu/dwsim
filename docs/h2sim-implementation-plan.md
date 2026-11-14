# H2sim 程序改造执行规划（AI Agent 可照此执行）

- 仓库：`/Users/wcx/ongoing/H2sim`（fork of DWSIM 9.0.5，GPLv3）
- 基线 commit：`1abf72d1b`（`v9.0.5-69-g1abf72d1b`，与 `upstream/windows` 差异为 0）
- 背景文档（**执行前必读**）：
  - `docs/h2-electrolyzer-retrofit-analysis.md` —— 电解槽物理模型改造分析
  - `docs/h2sim-openplc-bridge-scope.md` —— 平台收敛与 OpenPLC 桥接范围
- 本文档是**可执行计划**。每个任务卡自成闭环：有前置、精确文件路径、步骤、可验证的验收标准、回滚方式。

---

## 0. 执行者须知（先读这一节，违反会导致返工）

### 0.1 已冻结的产品边界

| 项 | 已定 | 不要重新讨论 |
|---|---|---|
| 发行平台 | **Windows x64 + macOS（Apple Silicon 目标，走 Rosetta）** | 不要尝试恢复 Linux/Raspberry |
| OpenPLC 版本 | **只对 v4**（`autonomy-logic/openplc-runtime`） | 不要为 v3 做兼容 |
| Modbus 角色 | **H2sim 当从站（server）** | 不要实现 Modbus 主站 |
| OPC UA 角色 | **H2sim 当 client**（连 OpenPLC v4 的 server） | 不要实现 OPC UA server |
| 内部控制 | **保留**（PID / PythonController / EventSet / CauseAndEffect） | 不要删除现有控制能力 |
| 闭环验证 | **虚拟 I/O**（无真实 PLC 硬件） | 不要写"已通过硬件在环验证" |
| Pro 模块 | **保留**，但只在 Windows 经典 UI 存在 | 不要把 ProFeatures 加进 macOS 包 |

### 0.2 硬技术约束（踩了会编不过或跑不起来）

| 约束 | 事实 | 后果 |
|---|---|---|
| 目标框架 | 全仓 `.NET Framework v4.6.2`（少数 v4.6.1/v4.8） | **不要引入只支持 `netstandard2.1` / `net6.0+` 的 NuGet 包** |
| OPC UA 库 | `OPCFoundation.NetStandard.Opc.Ua.Core` 必须锁 **`1.5.374.54`** | `2.0.2` 最低 `netstandard2.1`，net462 用不了 |
| Modbus 库 | `NModbus` **`3.0.81`**（有 `net46` 资产） | 不要用 `NModbus4`（已弃维） |
| macOS UI 后端 | `Eto.Platform.XamMac2 2.8.2` 是 net462 唯一可用的 macOS 后端；`Eto.Platform.macOS 2.8.3` 的 `Eto.macOS.dll` 是 `net6.0-macos10.15` | 不要试图在 net462 工程引用 `Eto.macOS.dll` |
| `ThermoCS` | `PlatformFiles/macOS/ThermoCS/*` 是 **Mach-O i386**，Apple Silicon 无法执行 | 必须在上层禁用，不要试图"修"二进制 |
| `packages/` 目录 | **当前不存在**，所有 `HintPath` 悬空 | 任何构建前必须先 `nuget restore` |
| Pro | `DWSIM.ProFeatures` 只被 `DWSIM/DWSIM.vbproj` 引用，依赖 `System.Windows.Forms` | 经典 UI 专属 |

### 0.3 编码与提交纪律

1. **不要修改 `WaterElectrolyzer.vb` 的计算逻辑**。它是 legacy，被 `ObjectType.WaterElectrolyzer` 和官方示例 flowsheet 引用。新增模型走新类（见 T-1.1）。
2. **新增单元操作一律走 `IExternalUnitOperation` + `ObjectType.External`**。理由是这条路径只需改 `.vbproj` 加编译项，不用动 `Enums.vb` / `FlowsheetBase.AddObjectToSurface` / `FlowsheetSurface_SkiaSharp` / `FormFlowsheet` / `SimulationObjectsPanel` 等 5+ 处。
3. **不改上游文件的行为**，只做"追加式"改动（加 `Compile Include`、加资源、加 `Case`）。确需改逻辑时，在 commit message 里写明理由。
4. 提交粒度：**一个任务卡一个 commit**，message 前缀 `T-x.x: `。跨任务的重构不要混在一起。
5. VB 工程 `OptionStrict Off`，但**新写的代码必须类型完整**，不要依赖隐式转换（现状 `WaterElectrolyzer.GetPropertyUnit` 的 `Case Else → Return 0.0` 就是靠 Off 混过的隐藏 bug，新代码不要学）。
6. **不要**在公开文档/commit/issue 里写真实厂站名、厂商投标原文。

### 0.4 每个任务卡的完成定义（DoD）

- [ ] 代码编译通过（Windows x64 Debug + Release 至少各一次）
- [ ] 任务卡里列出的验收命令/操作全部通过
- [ ] 新增的公开类型/方法有 XML doc 注释
- [ ] 涉及用户可见功能的，已按 §0.5 升版本
- [ ] 已运行 `bash scripts/clean.sh --dry-run` 确认没有把中间产物留在仓库

### 0.5 版本规约（工作区强制要求）

- 版本唯一来源：根目录 `VERSION` 文件（T-0.1 创建）
- 新功能升 `minor`，修复升 `patch`
- `VERSION`、产物文件名、`CHANGELOG.md` 的版本节三者必须一致
- 产物命名：`H2sim-<version>-windows-x64.exe`、`H2sim-<version>-macos-arm64.dmg`（macOS 若走 Rosetta 则用 `macos-x64`）

---

## 1. 阶段总览与执行顺序

```
Phase A 基建                     Phase B 桥接管道（MVP 主体）
┌────────────────┐              ┌──────────────────────────────┐
│ T-0.1 构建基底 │──────────────▶│ T-3.1 HeadlessDynamicsRunner │
│ T-0.2 平台收敛 │              │ T-3.2 SignalMap              │
└────────────────┘              │ T-3.3 ControlArbiter+PID增强  │
        │                       │ T-3.4 Modbus 从站桥           │
        │                       └──────────────────────────────┘
        │                                     │
        ├──────────────────┐                  │
        ▼                  ▼                  ▼
Phase C 模型          Phase D 扩展        Phase E 验证发布
┌──────────────┐      ┌──────────────┐    ┌──────────────┐
│ T-1.x 电化学 │      │ T-3.5 OPC UA │    │ T-5.x 闭环验证│
│ T-1b 动态    │      │ T-4.x BoP    │───▶│ T-6.x 打包发布│
│ T-2.x 物性   │      └──────────────┘    └──────────────┘
└──────────────┘                  ▲
        │                         │
        └───────▶ Phase F Apple Silicon（可全程并行，P6 前必须完成）
                  ┌──────────────┐
                  │ T-0.3 macOS  │
                  └──────────────┘
```

**强制顺序约束：**

| 约束 | 原因 |
|---|---|
| T-0.1 必须最先，且通过 **Gate 0** 才能继续任何任务 | 没有可构建的基线，后面全部无法验证 |
| T-3.1 必须在 T-3.2 / T-3.3 / T-3.4 之前 | 桥接需要无 UI 的动态循环载体 |
| T-3.3 必须在 T-3.4 / T-3.5 之前 | 桥只做 I/O，写入由仲裁器统一分发 |
| T-3.4 或 T-3.5 至少一个完成，才能开始 T-5.1 | 闭环验证需要一条可用的通讯链路 |
| T-0.3 必须在 T-6.2 之前 | macOS 包必须在 Apple Silicon 上能跑 |
| Phase C 与 Phase B 可并行 | 模型精度与管道时序互不阻塞 |

**建议里程碑：**

- **M1 = T-0.1 + Gate 0 → T-3.1 + T-3.2 + T-3.3 + T-3.4**：即可交付"OpenPLC 控电解槽模型"的最小闭环（用现有 WaterElectrolyzer 接口顶上）
- **M2 = M1 + Phase C**：模型精度到位
- **M3 = M2 + T-3.5 + T-4.x + Phase F**：功能完整
- **M4 = M3 + T-5.x + T-6.x**：可交付发行版

---

## 2. Gate 0：前置验证（必须先做，结果决定后续排期）

> 这不是可选项。这 4 项如果不成立，后面的技术路线要改。

### V-1 能否还原 NuGet 依赖

```sh
cd /Users/wcx/ongoing/H2sim
ls packages/ 2>/dev/null | wc -l     # 期望：0 或目录不存在
# Windows 上：
nuget restore DWSIM.sln -PackagesDirectory packages
# 或
msbuild DWSIM.sln -t:restore -p:Configuration=Release -p:Platform=x64
```

- ✅ 通过：`packages/` 下出现 `Eto.Forms.2.8.3`、`Eto.Platform.XamMac2.2.8.2`、`SkiaSharp.1.68.2.1`、`NLog.5.3.4` 等目录
- ❌ 失败：记录缺失包名，**停止后续所有任务**，先解决还原问题（可能需要在 `packages.config` 里补源或换 NuGet 源）

### V-2 能否编译并运行现有版本（Windows）

```sh
msbuild DWSIM.sln -p:Configuration=Release -p:Platform=x64 -m
```

- ✅ 通过：`DWSIM/bin/x64/Release/DWSIM.exe` 存在且能启动，能打开 `PlatformFiles/Common/samples/Water Electrolyzer.dwxmz`
- ❌ 失败：记录错误清单

### V-3 macOS 上 net462 + Mono 能否运行（**决定 macOS 路线**）

```sh
# 在 Apple Silicon 机器上
file DWSIM/bin/x64/Release/DWSIM.UI.Desktop.exe     # 期望：PE32 ... Intel 80386 Mono/.Net assembly
arch -x86_64 mono --version                          # 确认 Rosetta 下 Mono 可启动
arch -x86_64 mono DWSIM/bin/x64/Release/DWSIM.UI.Desktop.exe
```

- ✅ 通过（能起窗口）：走**方案 M1（全进程 Rosetta）**，按 T-0.3 执行
- ❌ 失败：macOS 主界面只能走**方案 M2（GTK3 渲染器）**或推迟到 .NET 8 迁移。此时 T-0.3 需要重写，且 T-6.2 的 macOS 打包方案要改成"仅 GTK3"

### V-4 `ThermoCS` 是否真的不可用（确认禁用它没有副作用）

```sh
file PlatformFiles/macOS/ThermoCS/charact1          # → Mach-O executable i386
ls PlatformFiles/macOS/ThermoCS/platform.txt        # → 不存在
lipo -info PlatformFiles/macOS/ThermoCS/charact1    # → Non-fat file ... i386
```

再确认 ThermoC 物性包在电解水场景里是否被用到：

```sh
cd /Users/wcx/ongoing/H2sim
grep -rl "ThermoC" PlatformFiles/Common/samples/ PlatformFiles/Common/tests/ 2>/dev/null
```

- ✅ 通过（无样例依赖 ThermoC）：T-0.3 里直接禁用，安全
- ⚠️ 有依赖：记录是哪些样例，禁用时在错误信息里提示"请改用其他物性包"

**Gate 0 输出物**：在 `docs/gate0-report.md` 里记录 V-1~V-4 的实际结果与结论。后续任务卡引用该结论。

---

## 3. Phase A：基建

### T-0.1 建立项目级工程基础设施

**目标**：让后续所有任务有版本、构建、清理、文档的落脚点。

**前置**：无

**新建文件**：

| 路径 | 内容要求 |
|---|---|
| `VERSION` | 一行，初始 `0.1.0`。这是唯一版本来源 |
| `CHANGELOG.md` | Keep a Changelog 格式，首节 `## [0.1.0] - <日期>`，写"基于 DWSIM 9.0.5（commit 1abf72d1b）派生，开始电解水制氢改造" |
| `AGENTS.md` | 按工作区规约写：本项目的构建命令、产物名、升版命令、目录角色表、红线、清理命令 |
| `CLAUDE.md` | 只写"先读 AGENTS.md"并 `@AGENTS.md` |
| `scripts/build.sh` | 见下 |
| `scripts/build.ps1` | Windows 版，同逻辑 |
| `scripts/clean.sh` | 见下，支持 `--dry-run` / `--deep` |
| `scripts/version.sh` | `bash scripts/version.sh minor` / `patch` → 改 `VERSION`、改所有 `AssemblyInfo` 的 `AssemblyVersion`、在 `CHANGELOG.md` 插新节 |
| `docs/development.md` | 环境要求、构建步骤、已知限制（引用 `gate0-report.md`） |

**`scripts/build.sh` 关键步骤（顺序不能变）**：

```sh
#!/usr/bin/env bash
set -euo pipefail
# 1. 检查工具链
command -v msbuild >/dev/null || { echo "需要 msbuild（Windows/mono）"; exit 1; }
# 2. 还原 NuGet（packages/ 当前不存在，必须做）
msbuild DWSIM.sln -t:restore -p:Configuration="${CONFIG:-Release}" -p:Platform="${PLATFORM:-x64}"
# 3. 编译
msbuild DWSIM.sln -p:Configuration="${CONFIG:-Release}" -p:Platform="${PLATFORM:-x64}" -m -v:m
# 4. 输出路径汇总（打印实际产物路径，供打包脚本用）
```

**`scripts/clean.sh` 删除范围**（按工作区规约）：

- 删：`build/`、`dist/`、`target/`、`.build/`、`bin/`、`obj/`、`__pycache__`、`.DS_Store`、`*sync-conflict*`、`*.user`、`*.suo`、测试报告与截图
- 默认不删：`packages/`（还原一次很慢）、`release/`、源码、锁文件
- `--deep` 额外删：`packages/`、`release/` 旧版本（保留最新）
- **绝不碰**：`.git/`、`.verysync/`、`PlatformFiles/**`、`docs/`、`fixtures/`

**修改文件**：

- `.gitignore`：确保包含 `/packages/`、`/release/`、`/build/`、`*.user`、`*.suo`、`/DistPackages/`（当前 `.gitignore` 已有 `obj`/`bin` 但**缺 `/packages/`**）

**验收**：

```sh
test -f VERSION && test -f CHANGELOG.md && test -f AGENTS.md && test -f CLAUDE.md
bash scripts/clean.sh --dry-run          # 只列不删，能正常退出
bash scripts/build.sh                    # 或 build.ps1，能出产物
grep -q "^/packages/" .gitignore
```

**禁止**：不要在这一步动任何源码。

---

### T-0.2 平台收敛到 Windows + macOS

**目标**：砍掉 Linux/Raspberry/Wine 的构建配置与二进制，减少构建时间与产物体积。

**前置**：T-0.1

**已核实的事实（照此执行，不要重新调查）**：

- `DWSIM.UI.Desktop.GTK` **不在 `DWSIM.sln` 里**（`grep -c 'DWSIM.UI.Desktop.GTK"' DWSIM.sln` = 0），它只是个孤儿目录 → **直接删目录即可，不用改 sln**
- `DWSIM.UI.Desktop.GTK3` 在 sln 里，且被 `DWSIM.UI.Desktop/Program.cs` 的 Linux 分支和 macOS 的 `Gtk3` 渲染器分支使用 → **保留**
- `ReleaseMac|x64` 在 sln 里映射到 **`Release|x64`**（不是 `ReleaseLinux`）

**操作清单**：

1. **删除目录**（不删文件引用，因为不在 sln）：
   `rm -rf DWSIM.UI.Desktop.GTK`
2. **删除平台二进制**：
   `rm -rf PlatformFiles/Linux PlatformFiles/Raspberry`
3. **`DWSIM.sln` 清理配置**：删除这些 solution 配置项及所有 `ProjectConfigurationPlatforms` 对应行
   - `DebugLinux|Any CPU`、`DebugLinux|x64`、`DebugLinux|x86`、`DebugLinux|wine32`
   - `ReleaseLinux|Any CPU`、`ReleaseLinux|x64`、`ReleaseLinux|x86`、`ReleaseLinux|wine32`
   - `ReleaseWine64|*`
   - 保留：`Debug|x64`、`Debug|x86`、`Release|x64`、`Release|x86`、`ReleaseMac|x64`、`ReleaseWinMac|x64`、`ReleaseWinMacP|x64`
   - ⚠️ **先备份** `cp DWSIM.sln DWSIM.sln.bak2`，删完必须能编译
4. **`DWSIM.PostBuildEvent` 里的 Linux 分支**：`DWSIM/DWSIM.vbproj` 的 `PostBuildEvent`（约 L3872 起）与 `DWSIM.UI.Desktop/DWSIM.UI.Desktop.csproj`（约 L411 起）里有 xcopy 到 `DistPackages\Linux\` 的语句 → 删除 Linux/Raspberry 相关行，保留 `Windows` 与 `macOS`
5. **`PlatformFiles/postcompile.bat`**：删除 `DistPackages\Linux`、`DistPackages\Windows_32` 相关行
6. **停编但保留源码**（不删目录、不删工程文件，仅从 sln 移除或改为不构建）：
   - `DWSIM.Apps.AzureServer`（云求解，与本地闭环无关）
   - `DWSIM.Simulate365`（第三方云服务）⚠️ 注意：`DWSIM.SharedClassesCSharp/FilePicker/Windows/WindowsFile.cs` 和 `DWSIM/Forms/MainWindow/FormMain.vb` 引用了它 → **先 grep 依赖，若强依赖则只停编不删工程**
7. **`DWSIM/DWSIM.vbproj` 的 `LINUX` 编译符号**：`ReleaseLinux|x64` 配置块里有 `<DefineConstants>LINUX</DefineConstants>`。若保留 `#If LINUX` 的代码分支（`DWSIM/Sources/*.vb` 里可能有），删除配置块会让 `LINUX` 永远为 false —— 这是**期望行为**，但要在 `docs/development.md` 记一句

**验收**：

```sh
test ! -d DWSIM.UI.Desktop.GTK
test ! -d PlatformFiles/Linux
test ! -d PlatformFiles/Raspberry
grep -c "Linux" DWSIM.sln                # 期望 0
msbuild DWSIM.sln -p:Configuration=Release -p:Platform=x64 -m    # 必须成功
# Windows 产物能启动，能打开 Water Electrolyzer.dwxmz
```

**回滚**：`git checkout -- DWSIM.sln` + `mv DWSIM.sln.bak2 DWSIM.sln`；目录从 git 恢复 `git checkout -- PlatformFiles/`

---

### T-0.3 macOS / Apple Silicon 适配

**目标**：让 macOS 版在 Apple Silicon 上可用（方案 M1：全进程 Rosetta），并让不可用的组件优雅失败而不是崩溃。

**前置**：Gate 0 的 V-3、V-4 通过

**已核实事实**（照此执行）：

| 事实 | 证据 |
|---|---|
| `libCoolProp/libipopt/liblpsolve55/libscintilla/libcoin{metis,mumps}/libPetAz.dylib` 全是 **x86_64** | `lipo -info` |
| `libSkiaSharp.dylib`(1.68.2.1) 是 **i386 + x86_64**，无 arm64 | `lipo -info` |
| `ThermoCS/*` 是 **i386**，且**无 `platform.txt`** | `file`、`ls` |
| `libipopt.dylib` 依赖 `/usr/local/gfortran/lib/libgfortran.3.dylib` + `libquadmath.0.dylib`，**均未随包** | `otool -L` |
| `libipopt.dylib` install name 是 `/Users/Daniel/Desktop/Ipopt-3.12.8/lib/libipopt.1.dylib` | `otool -L` |
| `Eto.Platform.macOS 2.8.3` 的 `Eto.macOS.dll` 只有 `net6.0-macos10.15` | nupkg 目录 |
| `Eto.Platform.XamMac2 2.8.2` 有 `lib/net462/Eto.XamMac2.dll` | nupkg 目录 |

**子任务**：

#### T-0.3.1 打包配置改 x86_64

- 在 `scripts/build.sh` / 打包脚本里，macOS 分支设 `Architectures=x86_64`
- 若上游的 `.app` 打包走 `Eto.Platform.macOS 2.8.3` 的 `build/Dmg.targets` / `BundleMono.targets`，确认它们能用 Rosetta（其 `Launcher64` 实测是 **x86_64**，正好符合 M1）
- 在 `docs/development.md` 写明：**macOS 版在 Apple Silicon 上通过 Rosetta 2 运行，首次启动前需 `softwareupdate --install-rosetta`**

#### T-0.3.2 禁用 ThermoC 物性包（macOS/Apple Silicon）

**改哪里**：`DWSIM.Thermodynamics.ThermoC/ThermoCPropertyPackage.cs` 构造函数（约 L93）

**怎么改**：在现有 32/64 位检查之前插入 CPU 架构检查。

```csharp
// 新增：Apple Silicon 上 ThermoCS 二进制是 i386，Rosetta 2 也不支持
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
    RuntimeInformation.OSArchitecture == Architecture.Arm64 &&
    !IsRunningUnderRosetta())
{
    throw new PlatformNotSupportedException(
        "ThermoC 物性包不支持 Apple Silicon（其原生二进制为 i386，" +
        "Rosetta 2 无法执行）。请改用其他物性包（如 Peng-Robinson / NRTL / Raoult）。");
}
```

同时：

- `IsRunningUnderRosetta()` 用 `sysctl -n sysctl.proc_translated` 判断（Rosetta 下 ThermoCS 的 i386 **仍然不可用**，因为 Rosetta 只处理 x86_64；所以这个判断实际上可以省略，直接按 OSX+Arm64 拒绝即可）
- **在物性包列表里隐藏它**：找到列出可用物性包的地方（`DWSIM/Forms/MainWindow/FormMain.vb` 的 `AddPropPacks()`、`DWSIM.FlowsheetBase/FlowsheetBase.vb` 的 `AddPropPacks()`），在 macOS 上跳过 `ThermoCPropertyPackage`
- **修复 `platform.txt` 缺失**：`ThermoCPropertyPackage.cs` L97–112 靠 `ThermoCS/platform.txt` 判 32/64 位，文件不存在时 `is64` 保持 `true`，与实际 32 位二进制矛盾。即使不删 ThermoCS，也要在 macOS 包里**不拷贝 `ThermoCS/` 目录**（12 MB）

#### T-0.3.3 清理 macOS 包内容

- `ThermoCS/`（12 MB，i386）**不打进 macOS 包**
- 确认 `DWSIM.ProFeatures.dll` 及依赖**不在 macOS 包里**（它只被经典 UI 引用，本来就该没有）
- `libipopt.dylib` 保留（Rosetta 下可用），但要在 `docs/development.md` 写明 `brew install gcc` 提供 `libgfortran.3`/`libquadmath.0`；并在启动日志里检测这两个库是否存在，缺失时提示"IPOPT 优化器不可用，其他功能正常"

#### T-0.3.4 验收

```sh
# 在 Apple Silicon 上
softwareupdate --install-rosetta --agree-to-license    # 若未装
# 启动 app，检查：
# 1. 主窗口能起来（Rosetta 下）
# 2. 新建 flowsheet，加 Water/Hydrogen/Oxygen，选 Peng-Robinson，能算
# 3. 打开 PlatformFiles/Common/samples/Water Electrolyzer.dwxmz，能算
# 4. 物性包列表里没有 ThermoC
# 5. 打开一个动态样例（Dynamic Simulation - Water Tank Level Control.dwxmz），能跑
```

**回滚**：ThermoC 的检查是纯新增分支，`git revert` 单个 commit 即可；打包配置改动同理。

---

## 4. Phase B：桥接管道（MVP 主体）

> 这一阶段的目标：**先把"外部控制器 ↔ 动态模型"整条管道打通并验证时序**，模型精度放到 Phase C。反过来做（先精修模型再搭管道）风险更大，因为时序问题会全部砸在最后。

### T-3.1 抽出 HeadlessDynamicsRunner

**目标**：把"动态仿真主循环"从两个 UI 窗体里抽出来，成为无 UI 依赖的可复用组件。这是整个二期**唯一必须改现有代码结构**的地方。

**前置**：T-0.1、T-0.2 完成；Gate 0 通过

**问题现状（已核实）**：

| 位置 | 行号 | 依赖 UI 的东西 |
|---|---|---|
| `DWSIM/Forms/FlowsheetComponents/FormDynamicsIntegratorControl.vb` `RunIntegrator()` | L265–660（约 396 行） | `Application.DoEvents()`（L421、L430）、`Flowsheet.RunCodeOnUIThread`、`ProgressBar1`、`lblCurrent`、`FormDynamics.UpdateControllerList`、`FormSurface.FControl.Invalidate`、`My.Resources.icons8_play`、`Flowsheet.Clone()` + `DirectCast(..., FormFlowsheet).Dispose()`（L395、L636） |
| `DWSIM.UI.Desktop.Editors/Dynamics/DynamicsIntegratorControls.cs` | 同构等价 | 同上（Eto 版） |
| `DWSIM.Automation/Automation.cs`（`Automation3`） | — | **完全没有动态 API** |
| `DWSIM.Automation/Flowsheet2.cs` | L92–96 | 只赋了 `DynamicsManager.RunSchedule`，**没有循环体** |

**新建项目**：`DWSIM.Automation.Dynamics/DWSIM.Automation.Dynamics.csproj`

- `OutputType=Library`，`TargetFrameworkVersion=v4.6.2`，`PlatformTarget=x64`
- 引用：`DWSIM.Interfaces`、`DWSIM.FlowsheetSolver`、`DWSIM.DynamicsManager`、`DWSIM.GlobalSettings`、`DWSIM.Logging`、`DWSIM.SharedClasses`、`DWSIM.Thermodynamics`、`DWSIM.UnitOperations`
- **不引用任何 UI 程序集**（这是验收关键）

**新建文件与职责**：

| 文件 | 内容 |
|---|---|
| `RunOptions.cs` | 见下方字段表 |
| `IDynamicsHost.cs` | UI 钩子抽象 |
| `HeadlessDynamicsHost.cs` | 空实现（所有回调 no-op），供 headless / 桥接使用 |
| `RunStats.cs` | `StepCount`、`SimTime`、`WallClockLag`（List 或环形缓冲）、`StepDurations`、`OverrunCount`、`Quality` |
| `DynamicsRunner.cs` | 主循环，从 `RunIntegrator` 移植 |
| `EventProcessor.cs` | 从 `ProcessEvents` / `ProcessCEMatrix`（当前在 Form 里 L155–200）抽出的逻辑 |
| `ControllerExecutor.cs` | 从主循环里 L517–540 的 PID/PythonController 执行段抽出 |

**`RunOptions` 字段（必须严格等于现有硬编码值作为默认）**：

```csharp
public class RunOptions
{
    public bool RealTime = false;                 // 现有：参数传入
    public double StepSeconds = 5.0;              // 现有：integrator.IntegrationStep 默认 5s
    public int ControlDecimation = 1;             // 现有：integrator.CalculationRateControl
    public int EquilibriumDecimation = 1;         // 现有：CalculationRateEquilibrium
    public int PressureFlowDecimation = 1;        // 现有：CalculationRatePressureFlow
    public double DurationSeconds = 0;            // 0 = 跑完 Duration
    public TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    public OverrunPolicy Overrun = OverrunPolicy.Skip;   // 新增
    public double OverrunFactor = 1.5;                   // 新增
    public Action<int, double> OnStep = null;            // (stepIndex, simTimeSeconds)
    public bool ReuseInitialState = true;         // 对应 schedule.UseCurrentStateAsInitial
    public string InitialStateId = "";            // 对应 schedule.InitialFlowsheetStateID
    public bool SubscribeToSchedule = true;       // 是否处理 EventList / CEMatrix
}
```

**`IDynamicsHost` 接口**：

```csharp
public interface IDynamicsHost
{
    void OnRunStarting(IFlowsheet fs, RunOptions opt);
    void OnPreStep(int stepIndex, double simTime);
    void OnPostStep(int stepIndex, double simTime, RunStats stats);
    void OnControllerIteration(ISimulationObject controller);
    void OnError(Exception ex, int stepIndex);
    void OnRunFinished(RunStats stats);
    bool ShouldAbort { get; }
    bool IsPaused { get; }
}
```

**移植时必须保留的行为（逐条对照，漏一条就是回归）**：

1. 校验 `DynamicsManager.ScheduleList` 含 `CurrentSchedule`，校验 `IntegratorList` 含 `schedule.CurrentIntegrator`，否则抛明确异常（对应现有 L269–277）
2. `integrator.RealTime = realtime`；`interval = RealTime ? RealTimeStepMs/1000.0 : IntegrationStep.TotalSeconds`
3. 首次步在 `!restarting && nstep==0` 时：`RestoreState(schedule.InitialFlowsheetStateID)` 或"用当前状态"，取决于 `UseCurrentStateAsInitial`
4. 子采样计数器初始化：`controllers_check = streams_check = pf_check = 100000`（**注意是 100000 不是 0**，这是现有行为，保证第一步就全部计算一次）
5. 每步前 `Flowsheet.ProcessScripts(IntegratorPreStep, ...)`，每步后 `IntegratorStep`
6. 子采样判定：`if counter >= rate * interval then counter = 0; ShouldCalculateX = true else false`，然后 `counter += interval`
7. `SolveFlowsheet(Flowsheet, GlobalSettings.Settings.SolverMode)`；**然后 `while CalculatorBusy do Task.Delay(200).Wait()`**
8. `exceptions.Count > 0` 时退出循环
9. 控制器执行顺序：先所有 `PIDController`，再所有 `PythonController`，每个都 `ProcessScripts(ObjectCalculationStarted/Finished/Error)`
10. 时间推进：`integrator.CurrentTime = CurrentTime.AddSeconds(interval)`；`nstep == -1` 时是减
11. `!realtime` 时才处理 `EventList` 与 `CauseAndEffectMatrix`
12. Historian：`EnableHistorian` 且有空间时，每步存 `GetSnapshot(ObjectData)` 压缩串；超过 `MaxHistorianItems` 移除最早的
13. `MonitoredVariableValues` 记录（`StoreVariableValues`，现有在 Form 里）
14. `SupressMessages = true` 在跑之前设，结束/出错后恢复
15. `Flowsheet.ClearLog()` 在开始

**移植时应当去掉的功能（因为属于 UI 或有害）**：

- `Application.DoEvents()`（L421、L430）—— 后台线程调这个是 WinForms 反模式，易重入
- `FlowsheetClone = Flowsheet.Clone()` + `DirectCast(..., FormFlowsheet)?.Dispose()`（L395、L636）—— headless 下不能克隆整个 flowsheet。`ProcessEvents` 里传给 `GetPropertyValuesFromEvents` 的 `FlowsheetClone` 参数改成 `Flowsheet` 本身（需验证 `GetPropertyValuesFromEvents` 是否真的要求独立实例；若要求，在 `EventProcessor` 里单独处理）
- `FormDynamics.UpdateControllerList()`、`FormSurface.FControl.Invalidate()`、`ProgressBar1`、`My.Resources.*` —— 移到 host 实现里

**新增功能（抽出来的顺带收益）**：

- `RunStats` 记录 `WallClockLag`（每步 `实际耗时 - StepSeconds`）与 `StepDurations`
- `OverrunPolicy` 处理：单步超过 `StepSeconds * OverrunFactor` 时按策略 `Skip` / `StretchGrid` / `Abort`
- 超时检测：`StartupTimeout` 用于初始稳态求解

**改两个 UI 窗体（各降到约 40 行）**：

- `FormDynamicsIntegratorControl.vb`：新增 `WinFormsDynamicsHost : IDynamicsHost`，`RunIntegrator(...)` 改为"构造 host + RunOptions + 调 `DynamicsRunner.Run`"，保留按钮状态、进度条、图表这些在 host 回调里做
- `DynamicsIntegratorControls.cs`：新增 `EtoDynamicsHost : IDynamicsHost`，同样处理

**加入 sln 与工程引用**：

- `DWSIM.sln` 加 `DWSIM.Automation.Dynamics` 工程（新 GUID），配置映射到 `Debug|x64` / `Release|x64` / `ReleaseMac|x64` 等
- `DWSIM.Automation` 加 `ProjectReference` 到它（供 T-3.2 使用）
- 两个 UI 工程加 `ProjectReference`

**验收**：

```sh
# 1. 编译不引入 UI 依赖
grep -i "System.Windows.Forms\|Eto" DWSIM.Automation.Dynamics/*.csproj    # 期望无输出
grep -i "System.Windows.Forms\|Eto" DWSIM.Automation.Dynamics/*.cs       # 期望无输出

# 2. 回归：现有 6 个动态样例结果与改造前一致
#    PlatformFiles/Common/samples/Dynamic Simulation - *.dwxmz
#    逐个用 Automation3 跑，比较关键 monitored variable 的最终值
#    判据：相对误差 < 1e-6（同机器、同输入）

# 3. Headless 能跑
#    写一个 console 测试：locate Flowsheet2, LoadFlowsheet, RunOptions{DurationSeconds=60}, DynamicsRunner.Run
#    期望：返回 RunStats.StepCount == 12, SimTime == 60, 无异常
```

**回归基准怎么建（重要）**：改造**之前**先用现有 UI 跑一遍 6 个动态样例，导出 monitored variables 的时间序列存到 `tests/baseline/dynamics/*.csv`。改造后用 headless 跑同一批，逐点比较。

**回滚**：新项目独立，UI 改动可 `git revert` 单 commit。

---

### T-3.2 SignalMap（信号映射）

**目标**：把"外部 PLC 点位 ↔ DWSIM 对象属性"的映射做成**可配置、随 flowsheet 保存**的数据，不在代码里硬编码 tag。

**前置**：T-3.1

**新建项目**：`DWSIM.H2sim.Io/DWSIM.H2sim.Io.csproj`

- `OutputType=Library`，`net462`，`x64`
- 引用：`DWSIM.Interfaces`、`DWSIM.SharedClasses`、`DWSIM.GlobalSettings`、`DWSIM.Logging`、`DWSIM.Automation.Dynamics`
- NuGet：`NModbus 3.0.81`（T-3.4 用）、`OPCFoundation.NetStandard.Opc.Ua.Client 1.5.374.54`（T-3.5 用）

**新建文件**：

| 文件 | 内容 |
|---|---|
| `SignalMap/SignalMap.cs` | 顶层容器：`List<ConnectionConfig>` + `List<Signal>` |
| `SignalMap/ConnectionConfig.cs` | 一条通讯连接：协议、地址、端口、认证 |
| `SignalMap/Signal.cs` | 一个点位映射（见字段表） |
| `SignalMap/SignalMapSerializer.cs` | 读写 XML，挂在 `IFlowsheet.ExtraProperties` 上随 flowsheet 保存 |
| `SignalMap/SignalMapValidator.cs` | 加载期校验（见下） |
| `Bridge/IPlantBridge.cs` | 桥抽象 |
| `Bridge/BridgeStatus.cs` | 连接状态、质量码、统计 |
| `Bridge/QualityCode.cs` | `Good` / `Bad` / `Uncertain` / `NotConnected` |

**`Signal` 字段（必须全部实现，缺一不可）**：

| 字段 | 类型 | 说明 | 必填 |
|---|---|---|---|
| `Id` | string | 唯一标识，如 `WE1.P_set` | ✅ |
| `Direction` | enum | `FromPlc`（PLC 写模型）/ `ToPlc`（模型被 PLC 读） | ✅ |
| `ObjectTag` | string | flowsheet 对象 Tag，如 `WE-1` | ✅ |
| `Property` | string | 对象属性名，如 `PowerSetpoint`；或 `PROP_MS_0` 这类标准属性 | ✅ |
| `Unit` | string | 工程单位，如 `kW`、`degC` | ✅ |
| `Type` | enum | `Int16` / `Int32` / `Float32` / `Bool` | ✅ |
| `Scale` | double | 原始值 = 工程值 × Scale + Offset | ✅（`Bool` 除外） |
| `Offset` | double | 同上 | ✅（`Bool` 除外） |
| `Deadband` | double | 变化超过此值才写（工程单位） | ⬜ 默认 0 |
| `WordOrder` | enum | `ABCD`/`CDAB`/`BADC`/`DCBA`，仅 `Int32`/`Float32` | ⬜ 默认 `ABCD` |
| `ClampOnWrite` | enum | `Reject`（保留旧值+报警）/ `Clamp` | ⬜ 默认 `Reject` |
| `ModbusArea` | enum | `HoldingRegister` / `InputRegister` / `Coil` / `DiscreteInput` | Modbus 必填 |
| `ModbusAddress` | int | 0-based（序列化时写 1-based 并注明） | Modbus 必填 |
| `OpcNodeId` | string | 完整 NodeId，如 `ns=2;s=PLC.H2.WE1.P_set` 或 `ns=2;i=1234` | OPC UA 必填 |
| `OpcNamespaceUri` | string | 用于生成 NodeId，默认 `urn:openplc:opcua:namespace` | OPC UA 可选 |
| `Enabled` | bool | 可临时禁用 | ✅ 默认 true |

**加载期校验（`SignalMapValidator`，任何一条失败就拒绝加载配置并给出可读错误）**：

1. `Id` 唯一
2. `ObjectTag` 在 flowsheet 里存在
3. `Property` 在该对象上的 `GetProperties(ALL)` 里存在（或 `IsDynamicProperty` 为 true）
4. **一个 `(ObjectTag, Property)` 最多被一个 `FromPlc` 信号指向** ← 最关键，防静默覆盖
5. Modbus 地址：同一 `Area` 内地址区间不重叠（`Int32` 占 2 个、`Float32` 占 2 个）
6. `OpcNodeId` 不与别人的冲突
7. `Scale != 0`（`Bool` 除外）
8. 若同时启用了 Modbus 与 OPC UA 两个连接，且同一属性既有 Modbus 又有 OPC UA 的 `FromPlc`，**报错**（跨协议冲突）

**持久化方式**：

- 挂在 `IFlowsheet.ExtraProperties`（`Dynamic.ExpandoObject`）的 key `"H2sim.SignalMap"` 下，值是一个 XML/JSON 字符串
- 为什么不用独立文件：这样 flowsheet 复制/分享时映射跟着走
- 需要确认 `ExtraProperties` 会被 `SaveData`/`LoadData` 序列化 —— 若不会被序列化，改为在 `Flowsheet2`/`FormFlowsheet` 的保存流程里追加一个 `<SignalMap>` 节点（参照 `FlowsheetBase.vb` L2676 `SimulationObject` 的写法）

**`IPlantBridge` 接口**：

```csharp
public interface IPlantBridge : IDisposable
{
    string Name { get; }
    BridgeStatus Status { get; }
    bool Connect(SignalMap map, IFlowsheet fs);
    void Disconnect();
    // 由 runner 每步调用：把 FromPlc 的最新值刷进模型
    void ApplyInputs();
    // 由 runner 每步调用后：把 ToPlc 的当前值发布出去
    void PublishOutputs();
    // 模型算失败时降级所有 ToPlc 的质量码
    void SetQuality(QualityCode q, string reason);
    event EventHandler<SignalErrorEventArgs> SignalError;
}
```

**线程模型（必须明确）**：

- 通讯线程（NModbus/OPC UA 内部线程）→ 只读写**双缓冲快照**，不碰模型
- 模型线程（`DynamicsRunner`）→ 每步开头 `ApplyInputs()` 从快照读，步骤结束 `PublishOutputs()` 写快照
- 用 `Interlocked` 版本号或 `ReaderWriterLockSlim`，**绝不**在通讯线程里调 `ISimulationObject.SetPropertyValue`
- 理由：DWSIM 的 `SetPropertyValue` 不是线程安全的，且会触发 `SetDirtyStatus`

**验收**：

```sh
# 1. 序列化往返
#    构造一个含 20 个 signal 的 map，Save → Load，逐字段相等
# 2. 校验器能拦住冲突
#    造两个 FromPlc 指向同一 (ObjectTag, Property) → Connect() 返回 false 且错误信息含 "conflict"
# 3. 地址重叠检测
#    造两个 HoldingRegister 地址 100 和 101，其中一个是 Int32 → 报错
```

**禁止**：不要在这一步实现任何协议细节。

---

### T-3.3 ControlArbiter + PID 增强

**目标**：解决"内部 PID 与外部 PLC 同时想控同一个 MV"的所有权问题，并让 PID 具备无扰切换能力。

**前置**：T-3.1、T-3.2

**问题现状（已核实）**：

- `DWSIM.UnitOperations/Controllers/PIDController.vb` 只有 `ManualOverride`（L106）与 `Active`（L104）
- `ManualOverride = True` 时，`Calculate()`（L722–794）走 else 分支只更新 `Output`，**不跟踪 `ITerm`** → 切回自动有积分冲击
- `ITerm` 只有硬钳位（L752–756），没有反算抗饱和
- 没有输出速率限制、没有设定值斜坡

**3.3.1 新增枚举**（`DWSIM.Interfaces/Enums.vb`）

```vb
Public Enum ControlOwnership
    Local = 0      ' 内部 PID/Python 写
    External = 1   ' 外部 PLC 写，内部控制器只读（Tracking）
    Manual = 2     ' 都不写，人工在 UI 改
    Shadow = 3     ' 内部算但不写，记录与外部值偏差
End Enum
```

⚠️ 加枚举值要在 `Enums.vb` 末尾追加，**不要插在中间**（现有 enum 序列化依赖顺序 / 数值）。

**3.3.2 `PIDController` 增强**（`DWSIM.UnitOperations/Controllers/PIDController.vb`）

新增属性：

| 属性 | 类型 | 默认 | 作用 |
|---|---|---|---|
| `TrackingMode` | bool | false | true 时不写 MV，但按当前 MV 反算 `ITerm`，实现无扰 |
| `OutputRateLimit` | double | 0（不限） | MV 变化率上限，工程单位/s |
| `SetpointRampRate` | double | 0（不限） | 设定值斜坡速率，工程单位/s |
| `BackCalculationGain` | double | 0（关闭） | 反算抗饱和系数（1/Tt） |
| `Ownership` | ControlOwnership | Local | 所有权状态 |
| `ExternalMV` | double | 0 | `External`/`Shadow` 下外部写入的 MV 值（供显示与偏差统计） |

`Calculate()` 改造要点：

```
1. UpdateVars() 后，若 Ownership = External：
     - 读 ExternalMV（由仲裁器写入）
     - 反算 ITerm：ITerm = (MV_eng - Offset/BaseSP - PTerm - Kd*DTerm) / Ki
       （即让 u = MV 成立，解出积分项）
     - 不调用 ManipulatedObject.SetPropertyValue
2. 若 Ownership = Shadow：
     - 正常算 Output/OutputAbs
     - 记录 (OutputAbs - ExternalMV) 到 ShadowDeviation 历史
     - 不写 MV
3. 若 Ownership = Manual：既不读 ExternalMV 也不写 MV
4. Local：同现有逻辑 + 新增 RateLimit / Ramp / BackCalc
5. SetpointRamp：AdjustValue 按 SetpointRampRate 向 SPValue 逼近
6. OutputRateLimit：OutputAbs 与前一步的差不超过 RateLimit * timestep
7. BackCalculationGain > 0 时：ITerm -= BackCalcGain * (OutputAbs - OutputAbs_clamped) * timestep
```

**必须保证的向后兼容**：所有新属性默认值 = 现有行为（`TrackingMode=false`、两个 Rate=0、`BackCalcGain=0`、`Ownership=Local`）。这样现有 flowsheet 行为完全不变。

**3.3.3 `ControlArbiter`**（新建 `DWSIM.H2sim.Io/Control/ControlArbiter.cs`）

职责：

1. 从 `SignalMap` 推导出"哪些 MV 被外部控" → 对应 PID 设 `Ownership = External`
2. 提供 `SwitchOwnership(objectTag, property, newOwner)`，**原子**完成无扰切换
3. 运行期检测冲突：同一 MV 第二次被外部写入 → 拒绝 + 记 `SignalError` + 报警
4. Shadow 模式下的偏差统计（每步记录）

无扰切换流程（`External → Local`）：

```
1. 读当前实际 MV（工程单位）
2. 用反算公式预置 ITerm
3. 同步 LastError = 0、CurrentError = 0（避免 DTerm 冲击）
4. 置 Ownership = Local
以上 4 步必须在同一个模型步的间隙完成，不允许中间插入 SolveFlowsheet
```

**验收**：

```csharp
// 1. 无扰切换：External 稳定在 MV=50%，切到 Local，MV 不应跳变超过 RateLimit
// 2. Shadow 偏差：外部 MV 与内部算出的 MV 写入 ShadowDeviation，可读
// 3. 冲突检测：两次写同一 MV，第二次抛 SignalError，MV 保持第一次的值
// 4. 回归：现有 PID 样例（Dynamic Simulation - Water Tank Level Control.dwxmz）
//    结果与改造前完全一致
```

**禁止**：不要把联锁逻辑放进 PID 或仲裁器（见 T-1.4）。

---

### T-3.4 Modbus TCP 从站桥

**目标**：H2sim 作为 Modbus TCP 从站（server），OpenPLC v4 用 `modbus_master` 主动轮询。

**前置**：T-3.1、T-3.2、T-3.3

**为什么是从站**：符合"模型扮演虚拟现场设备"的惯例。物理世界里 PLC 永远是主站去读传感器、写执行器；这样将来把模型换成真实设备时 PLC 程序不用改。

**新建文件**：

| 文件 | 内容 |
|---|---|
| `Bridge/Modbus/ModbusTcpServerBridge.cs` | `IPlantBridge` 实现 |
| `Bridge/Modbus/ModbusSlaveDataStore.cs` | `NModbus` 的 `IModbusSlaveDataStore` 实现，桥接双缓冲快照 |
| `Bridge/Modbus/ModbusAddressMap.cs` | 把 `Signal.ModbusArea` + `ModbusAddress` 映射到 NModbus 的区段 |
| `Bridge/Modbus/ModbusScaler.cs` | 定点缩放、`WordOrder` 处理 |

**NModbus 用法（已核实 `net46` 资产含从站 API）**：

```csharp
var factory = new ModbusFactory();
var network = factory.CreateSlaveNetwork(tcpListener);   // 已实测存在
var slave = factory.CreateSlave(unitId);
slave.DataStore = new ModbusSlaveDataStore(snapshot);    // 我们的实现
network.AddSlave(slave);
await network.ListenAsync();                             // 后台 Task
```

**关键实现细节**：

1. **不放 REAL**：OpenPLC v4 的 `modbus_slave` 分段无 float 段。所有模拟量用 `Int16` + `Scale`/`Offset`。
2. **地址分段对齐 OpenPLC v4**：`docs/io-map.md` 里定死一张表。参考分段：
   ```
   %IW  30001-30024  : 传感器模拟量（Input Register，只读）
   %IX  10001-10020  : 传感器布尔量（Discrete Input，只读）
   %QX  00001-00020  : 执行器布尔量（Coil，读写）
   %QW  40001-40030  : 设定值/执行器模拟量（Holding Register，读写）
   ```
3. **点位连续性**：OpenPLC `modbus_master` 按 FC 分组批量读（`modbus_master_utils.py` 的 `read_requests[fc]`），**同区域内不连续的点会被拆成多个请求**→ 拉长扫描周期。所以 `SignalMapValidator` 要额外检查"同一 Area 内地址尽量连续"，并在 `docs/io-map.md` 里规定。
4. **质量码**：Modbus 无质量码。约定：模型算失败时，所有 `ToPlc` 的模拟量置 `-32768`（Int16 哨兵值），并在单独的 Coil（如 `ALM.ModelFault`）置位。`docs/io-map.md` 必须写明这个约定。
5. **零阶保持**：`PublishOutputs()` 写入快照后，从站在下一个主站请求前保持不变。**不做插值**。
6. **线程安全**：`ModbusSlaveDataStore.ReadHoldingRegisters` 等回调在 Modbus 线程里跑，只读快照；`PublishOutputs` 在模型线程写快照。用版本号双缓冲或 `ReaderWriterLockSlim`。

**配置示例（写进 `docs/io-map.md`）**：

```xml
<Connection Name="OpenPLC" Protocol="ModbusTcpServer" Host="0.0.0.0" Port="502" UnitId="1" Enabled="true"/>
```

**验收**：

```sh
# 1. 自环测试（不依赖 OpenPLC）
#    写一个 test 程序：起 ModbusTcpServerBridge + 用 NModbus client 读 %IW
#    期望：读到结构化好的 Int16

# 2. 缩放正确性
#    T_stack = 85.3 degC，Scale=10 → 原始值 853；反向读回 85.3

# 3. 质量码
#    故意让模型抛异常 → 读到 -32768，ModelFault coil = true

# 4. 地址连续性
#    用 Wireshark 抓包，确认一轮轮询的请求数 == 区域数（而不是点位数）
```

**`docs/io-map.md` 必须写清**：地址表、每个点的类型/量程/缩放/单位/读写方向、质量码约定、与 OpenPLC v4 `%IW/%QX` 的对应关系。

---

## 5. Phase C：电解槽模型

> 详细物理依据见 `docs/h2-electrolyzer-retrofit-analysis.md`。本节只列执行要点。

### T-1.1 电解槽 Unit Operation 骨架

**目标**：建立可扩展的电解槽类层次，**不改** `WaterElectrolyzer.vb`。

**前置**：T-0.1

**新建目录**：`DWSIM.UnitOperations/UnitOperations/Electrolyzers/`

| 文件 | 内容 |
|---|---|
| `ElectrolyzerUnitOpBase.vb` | 公共基类。**继承 `UnitOpBaseClass`，不继承 `CleanEnergyUnitOpBase`** |
| `IElectrochemicalModel.vb` | 电压模型接口：`Function CellVoltage(i As Double, T As Double, pCat As Double, pAn As Double) As VoltageBreakdown` |
| `VoltageBreakdown.vb` | 结构：`U_ocv`、`U_act`、`U_ohm`、`U_conc`、`U_cell`、`U_rev`、`U_tn` |
| `ElectrolyzerCalculator.vb` | 功率↔电流闭合迭代、Faraday、crossover、KPI 计算 |
| `ModelPEM_Springer.vb` | PEM 半经验模型 |
| `ModelAlkaline_SemiEmpirical.vb` | ALK 半经验模型 |
| `PEMElectrolyzer.vb` | PEM 组装类 |
| `AlkalineElectrolyzer.vb` | ALK 组装类 |

**`ElectrolyzerUnitOpBase` 的关键成员**：

```vb
' 必须覆盖：CleanEnergyUnitOpBase 的 IsSource=True 对电解槽是错的
' （会让它被 FlowsheetSolver.GetSolvingList 放进 lists(0) 最先算，见 L1030）
Public Overrides ReadOnly Property IsSource As Boolean = False

Public Overrides Property ObjectClass As SimulationObjectClass = SimulationObjectClass.Electrolyzers
' 注：SimulationObjectClass.Electrolyzers = 18 已存在但无人使用（Enums.vb L515），直接用

Public Overrides ReadOnly Property Prefix As String = "PEMEL-"   ' 或 "ALKEL-"

' 参数容器（复用 PEMFC 的范式，见 T-1.2）
Public Property Parameters As Dictionary(Of String, ElectrolyzerModelParameter)

' 计算入口
Public Overrides Sub Calculate(Optional args As Object = Nothing)

' 输入/输出连接器：1 进水 + 1 能量入 + 2 物料出（H2-rich / O2-rich）+ 1 冷却水入 + 1 冷却水出
Public Overrides Sub CreateConnectors()
```

**⚠️ 连接器设计要注意**：`CreateConnectors()` 里连接器数量必须稳定，因为 `FlowsheetSurface_SkiaSharp.vb` L2625 和 `FlowsheetBase.vb` L1794 对 `ObjectType.WaterElectrolyzer` 有硬编码的 40×40 特例。新类走 `ObjectType.External`，不受这些影响，但**改连接器数量会让已保存的 flowsheet 加载异常** → 从第一版就把连接器定死。

**验收**：

```sh
# 1. 编译通过，能在面板里出现（ExternalUnitOperation 通过反射发现）
# 2. 拖到画布上，连接器正确
# 3. 保存 → 重新加载，参数不丢
# 4. Calculate 只做占位，不报错（真实计算在 T-1.2）
```

---

### T-1.2 电化学模型 + KPI

**前置**：T-1.1

**参数容器**（复用 `PEMFuelCellModelParameter` 的形状，见 `CleanEnergies/PEMFuelCellUnitOpBase.vb` L31–72）：

```vb
Public Class ElectrolyzerModelParameter
    Public Property Name As String
    Public Property Description As String
    Public Property Value As Double
    Public Property Units As String
    Public Property ValuesX As List(Of Double)   ' 支持曲线
    Public Property ValuesY As List(Of Double)
End Class
```

PEM 参数集（来源：Bzovsky 2024，DOI `10.3311/PPch.38170`）：

```
A_cell=1000 cm2, N_cells=182, i_max=2.5 A/cm2, V_cell_max=2.2 V, i_min_frac=0.10
i0_an_ref=2.16e6 A/cm2, E_act_an=76000 J/mol, alpha_an=0.5, i_L=6 A/cm2
t_mem=0.0178 cm, lambda_mem=22, rho_electrode=7.5 mOhm*cm, t_electrode=0.1 cm
P_cat=30 bar, P_an=2 bar, T_op=353.15 K, etaF_ref=0.99
C_stack, m_stack, Cp_stack, U_loss_area, cooling_mode
```

**计算流程**（`Calculate()` 内，严格按此顺序）：

```
1. 读输入：P_stack [kW]（来自 EnergyStream）或 I_stack [A]，按 SetpointMode 二选一
2. 用现有 PropertyPackage 算 U_rev(T)、U_tn(T)
   - 复用 WaterElectrolyzer.vb L415-455 的 ΔG/ΔH 算法（含 NIST Cp 积分）
   - 但两侧压力分开用：pCat、pAn
3. Nernst：U_ocv = U_rev + RT/(2F) * ln(p_H2 * sqrt(p_O2) / a_H2O)
4. 内层迭代求电流（关键，替代现有的 I=P/V）：
     U_cell(i) = U_ocv + U_act(i,T) + U_ohm(i,T,lambda) + U_conc(i,T)
     P_calc(i) = U_cell(i) * i * A_cell * N_cells
     用 DWSIM.Math/Brent.vb 解 P_calc(i) = P_target，区间 [i_min, i_max]
     i_min = i_max * i_min_frac
     不收敛 → 抛可诊断异常（含区间端点值）
   备选：I = f(P,T) 多项式拟合（Bzovsky 路线），作为 UI 可选"快速模式"
5. Faraday：etaF = f(i, T, 循环流量)；n_H2 = etaF * i*A*N/(2F)
   气体穿透：n_H2_cross = k_perm(T, dP) / max(i, i_epsilon)   ← 低载时相对影响放大
6. 物料：H2 侧（H2 + 饱和水 + 微量 O2）用 pCat 做 flash；
        O2 侧（O2 + 饱和水 + 微量 H2 + 过量循环水）用 pAn 做 flash
7. 能量：Q_gen = N * i * A * (U_cell - U_tn) [kW]
   稳态：Q_cool 由目标 T 反算
   动态：见 T-1.4
8. KPI 全部写进 GetProperties / GetReport / Inspector：
   CellVoltage, CurrentDensity, StackVoltage, Current, Power,
   U_rev, U_tn, U_act, U_ohm, U_conc,
   FaradayEfficiency, H2Purity, H2inO2_pct, O2inH2_pct,
   SEC_kWh_per_kg_H2, Efficiency_HHV, Efficiency_LHV, CoolingDuty, StackTemperature
```

**必须修的现有 bug**：`GetPropertyUnit` 里 `Case Else → Return 0.0`（`WaterElectrolyzer.vb` L132–133）。新类里 `Case Else` 返回 `""`。

**单位扩展**（`DWSIM.Interfaces/Enums.vb` + `DWSIM.SharedClasses/UnitsOfMeasure/SystemsOfUnits.vb`）：

- 现有 `UnitOfMeasure` 枚举末尾是 `specificpower = 71`（L225），**追加**：
  ```vb
  voltage = 72
  current = 73
  currentDensity = 74
  ```
- `IUnitsOfMeasure.vb` 加三个 `Property`
- `SystemsOfUnits.vb` 要给 7 个类各加三处（`SI` L500、`SI_ENG` L589、`SIUnits_Custom1~5` L683/773/863/953/1043、`English` L1133、`CGS` L1225）：
  - 属性定义（如 `Public Property voltage As String = "V"`）
  - `GetUnitType` 的 `Case`（L295 起）
  - `GetCurrentUnits` 的 `Case`（L401 起）
  - 构造函数里 `.voltage = "V"` 等
- 具体单位串：`SI`/`SI_ENG`/`Custom1~5` 用 `V` / `A` / `A/cm2`；`English`/`CGS` 同（电学单位本来就统一）

**验收**：

```sh
# 1. 极化曲线：给定 T=60/70/80 degC，画 U_cell vs i
#    判据：与 Bzovsky 2024 Fig.2 趋势一致，U_cell 相对误差 < 2%
# 2. 部分负荷：10% ~ 100% 功率，算 SEC
#    判据：与 Bzovsky 2024 Fig.5 趋势一致，SEC 误差 < 3%
# 3. 单位转换：切 SI / English / CGS，V/A 数值不变
# 4. 质量守恒：sum(inlet mass) == sum(outlet mass)，相对误差 < 1e-8
# 5. 能量守恒：P_in == Q_chem(H2 LHV) + Q_cool + Q_loss + dU/dt
```

---

### T-1.3 联锁与安全（必须独立于所有权）

**前置**：T-1.2

**要求**：联锁放在**模型侧**（`ElectrolyzerUnitOpBase`），不在 PID、不在仲裁器、不在桥里。这样 `Local`/`External`/`Shadow` 三种模式下联锁一律生效。

**联锁清单**：

| 条件 | 阈值 | 动作 | 复位 |
|---|---|---|---|
| H2-in-O2 高报 | > 2 vol% | 报警位 `ALM.H2inO2_H` | 自动（降回 < 1.5%） |
| H2-in-O2 跳车 | > 4 vol%（爆炸下限） | 切断功率（`PowerSetpoint = 0`）+ 置 `WE.Trip` + 打开放空/氮气吹扫 | 手动 `Reset` |
| O2-in-H2 高报 | > 2 vol% | 报警 | 自动 |
| 堆温超限 | > 90 °C（PEM）/ > 95 °C（ALK） | 降功率（降到 0） | 手动 |
| 阴极-阳极压差超限 | > 允许值（参数化） | 跳车 | 手动 |
| 水位低低 | < 5% | 跳车 | 手动 |

**关键**：跳车必须 `Latch`（锁存），防止 PLC 反复重启到危险工况。`Reset` 只能人工触发（UI 按钮或 PLC 的 `WE.Reset` 上升沿）。

**验收**：

```sh
# 1. 人为把 H2inO2 推到 4.5%（改参数或强制），确认 Trip 锁存
# 2. 清条件后 Trip 仍锁存，直到 Reset
# 3. External 模式下（PLC 在控）触发联锁，确认功率确实归零并且 PLC 看到 Trip 位
```

---

### T-1.4 电解槽动态支持

**前置**：T-1.3、T-3.1（需要 runner 的步长信息）

**实现三个 override**（参照 `DWSIM.UnitOperations/UnitOperations/Vessel.vb` L35/L135/L202 的写法）：

```vb
Public Overrides ReadOnly Property SupportsDynamicMode As Boolean = True
Public Overrides ReadOnly Property HasPropertiesForDynamicMode As Boolean = True

Public Overrides Sub CreateDynamicProperties()
    AddDynamicProperty("Stack Temperature", "Current stack temperature", 353.15, UnitOfMeasure.temperature, 1.0.GetType())
    AddDynamicProperty("Cooling Water Flow", "Coolant mass flow", 0, UnitOfMeasure.massflow, 1.0.GetType())
    AddDynamicProperty("Coolant Inlet Temperature", "Coolant supply temperature", 298.15, UnitOfMeasure.temperature, 1.0.GetType())
    AddDynamicProperty("H2 in O2", "Vol% H2 in oxygen stream", 0, UnitOfMeasure.none, 1.0.GetType())
    AddDynamicProperty("O2 in H2", "Vol% O2 in hydrogen stream", 0, UnitOfMeasure.none, 1.0.GetType())
    AddDynamicProperty("Enable Thermal Model", "Enable stack thermal inertia", True, UnitOfMeasure.none, True.GetType())
End Sub

Public Overrides Sub RunDynamicModel()
    ' 取步长（照 Vessel.vb L202 的写法）：
    Dim integrator = FlowSheet.DynamicsManager.IntegratorList(
        FlowSheet.DynamicsManager.ScheduleList(FlowSheet.DynamicsManager.CurrentSchedule).CurrentIntegrator)
    Dim timestep = integrator.IntegrationStep.TotalSeconds
    If integrator.RealTime Then timestep = integrator.RealTimeStepMs / 1000.0

    ' 子步积分（必须有！stack 热惯性小，大步长显式欧拉会发散）
    Dim tau = C_stack / (U_loss_area + m_coolant * Cp_coolant)
    Dim dt_sub = Math.Min(timestep, tau / 10.0)
    Dim n_sub = Math.Max(1, CInt(Math.Ceiling(timestep / dt_sub)))

    For k = 1 To n_sub
        ' 半隐式欧拉：T_new = (C*T_old + dt*(Q_gen + Q_in)) / (C + dt*(UA + mCp))
        ...
    Next

    SetDynamicProperty("Stack Temperature", T_new)
    SetDynamicProperty("H2 in O2", h2inO2)
    ...
End Sub
```

**关键约束**：

- `dt_sub` 必须**内部子步**，不改框架的 `IntegrationStep`。理由：现有动态框架是"每个 UO 自己用固定步长显式积分"，改框架风险太大（见 T-3.1 的回归要求）
- 冷却模型两种：`CoolingMode = "WaterFlow"`（m_cw、Cp、T_in 由输入流给出）或 `"LyeLoop"`（碱液循环 + 板换，**变流量**，因为文献 R7 说变流量比定流量多产氢 8–12%）
- `Enable Thermal Model = False` 时退化为等温（`T` 由输入流决定），用于匹配稳态模型

**验收**：

```sh
# 1. 阶跃：功率从 50% 到 100%，T_stack 上升，时间常数与 tau 量级一致
# 2. 不发散：IntegrationStep = 60s 时仍稳定（靠子步）
# 3. 与 R1（Dinh 2021, DOI 10.1016/j.ijhydene.2021.11.126）的热模型对比：
#    稳态温度误差 < 1 K，阶跃响应时间常数误差 < 10%
# 4. 等温模式与稳态模型结果一致（Enable Thermal Model = False 时）
```

---

### T-2.1 KOH/NaOH 物性与电导率

**前置**：T-0.1

**化合物库**（`DWSIM.Thermodynamics/Assets/Databases/electrolyte.xml`）：

- 现有 84 个物种，已有 `Hydroxide`、`Potassium Ion`、`Sodium Hydroxide`
- **缺 `Potassium Hydroxide`** → 参照现有 `Sodium Hydroxide` 条目补一条：
  - `DelGF`、`DelHF`、`Cp0`（从 NIST 取）
  - `IsIon = false`、`PositiveIon = "Potassium Ion"`、`NegativeIon = "Hydroxide"`
  - 电解质相关字段
- 顺带补 `Potassium Carbonate`（ALK 电解液吸收 CO2 后的产物，长周期杂质）—— 已有 `Potassium Carbonate (1)~(4)`，确认是否可用

**电解液电导率**：**不改物性框架**（改 `ICompoundConstantProperties` 会牵动序列化、数据库、编辑窗体）。

- 在 `ModelAlkaline_SemiEmpirical.vb` 里内置关联式：σ = f(质量分数, T)
- 参数来源：R9（Energies 15(9) 3452, DOI `10.3390/en15093452`）的关联式汇编
- 把关联式与参数写进 `docs/parameters/alkaline-conductivity.md`，注明出处

**H2/O2 溶解**：`henry_constants.csv` 已有 O2、H2 数据，`AUX_KHenry` 可用（`ElectrolyteIdeal.vb` L573 已在用）。把溶解量纳入 H2 收率损失。

**验收**：

```sh
# 1. 在物性包编辑界面能选到 Potassium Hydroxide
# 2. 配 30 wt% KOH 水溶液，能算密度/粘度/沸点（与手册对比，误差 < 5%）
# 3. 电导率：30 wt% KOH @ 80 degC，与文献值对比（约 1.3 S/cm），误差 < 10%
```

---

## 6. Phase D：扩展

### T-3.5 OPC UA Client 桥

**目标**：H2sim 作为 OPC UA client，连 OpenPLC v4 的 OPC UA server。

**前置**：T-3.2、T-3.3

**已核实的协议事实**：

| 事实 | 影响 |
|---|---|
| OpenPLC v4 的 OPC UA 插件是 `asyncua` 写的**纯 server**（`plugin.py` 里只有 `OpcuaServerManager`），没有 client 角色 | **H2sim 只能当 client** |
| NodeId 形态：配置给 `node_id`（如 `PLC.Example.real_var`），运行时生成 `ns=<idx>;s=<node_id>` | 映射要支持**字符串 NodeId**，不能只支持 `i=` 数字 |
| 默认命名空间 URI：`urn:openplc:opcua:namespace` | 支持配置 |
| 默认端点：`opc.tcp://localhost:4840/openplc/opcua` | 支持配置 |
| 写权限按角色：`viewer`=只读、`operator`=读写、`engineer`=全权；每节点 `"r"`/`"rw"` | **账号必须至少 `operator`**，否则写入被 `PreWrite` 拒绝 |
| 支持订阅（`SUBSCRIPTION_IMPLEMENTATION.md`） | **优先用 Subscription + MonitoredItem**，不要高频 Read |
| 安全策略：`None` / `Basic256Sha256+Sign` / `Basic256Sha256+SignAndEncrypt`；认证：`Anonymous`/`Username`/`Certificate` | MVP 支持 `None`+`Anonymous` 和 `Basic256Sha256+Sign`+`Username` 两种组合 |

**新建文件**：

| 文件 | 内容 |
|---|---|
| `Bridge/OpcUa/OpcUaClientBridge.cs` | `IPlantBridge` 实现 |
| `Bridge/OpcUa/OpcUaSessionFactory.cs` | 建会话、选安全策略、证书 |
| `Bridge/OpcUa/OpcUaSubscriptionManager.cs` | 订阅管理（`OnChange` 回调写快照） |
| `Bridge/OpcUa/OpcUaNodeResolver.cs` | `ns=<idx>;s=<id>` 与 `ns=<idx>;i=<n>` 双向解析 |
| `Bridge/OpcUa/OpcUaDiagnostics.cs` | 会话状态、重连、延迟统计 |

**实现要点**：

1. **版本锁死** `OPCFoundation.NetStandard.Opc.Ua.Client 1.5.374.54`（有 `netstandard2.0` 资产）
2. **依赖冲突预检**：`Opc.Ua.Core` 会带入 `Microsoft.Extensions.Logging`、`System.Memory`、`System.Buffers`、`System.Runtime.CompilerServices.Unsafe`。DWSIM 现有 `Unsafe 6.1.0`（`DWSIM.UnitOperations/packages.config`）→ **先在空项目验证 net462 + 这组包能编能跑**，再集成。必要时在 `app.config` 加 `bindingRedirect`，并加 `AppDomain.AssemblyResolve` 兜底
3. **订阅优先**：`ToPlc` 的读取用 Subscription，`FromPlc` 的写入用 `WriteAsync`（不可订阅写）
4. **质量码**：直接把 OPC UA 的 `StatusCode` 映射到 `QualityCode`（`Good` / `Bad` / `Uncertain` / `NotConnected`）
5. **写权限探测**：`Connect()` 成功后，对一个 `FromPlc` 节点做一次"写回原值"的探测；失败则返回明确错误"账号权限不足，需要 operator 或 engineer 角色"
6. **重连**：会话断开后按指数退避重连（1s/2s/4s/8s，上限 30s），期间所有 `ToPlc` 质量码置 `NotConnected`
7. **时间戳**：记录每个值的 `SourceTimestamp`，与本地墙钟比较，偏差过大时报警

**验收**：

```sh
# 1. 对 OpenPLC v4（Docker）：
#    docker run -d -p 8443:8443 -p 4840:4840 ghcr.io/autonomy-logic/openplc-runtime:latest
#    配置 OPC UA 插件启用，加几个 REAL 变量
#    H2sim 连上去，能读到值、能写入
# 2. 权限：用 viewer 账号连接 → Connect 返回 false 且错误含 "权限"
# 3. 断线重连：kill 容器 → 质量码变 NotConnected → 重启容器 → 自动恢复
# 4. 订阅延迟：与 Modbus 对比（同一模型，同一信号）
```

---

### T-4.1 BoP 对象

**前置**：T-1.2

**新建**（同样走 `IExternalUnitOperation`）：

| 文件 | 内容 | 实现方式 |
|---|---|---|
| `UnitOperations/Electrolyzers/H2Purification.vb` | H2 纯化（TSA/PSA/DeOxo） | **黑箱模型**：去 O2/H2O 到目标 ppm + 再生气耗 + 压降。**不要一上来做严格吸附**（`DWSIM.UnitOperations` 里没有吸附 UO，做严格的会变成大工程） |
| `UnitOperations/Electrolyzers/HydrogenStorage.vb` | 储罐/管束 | P-V-T（PR 或简化 Z）+ 充装/取用 + 压力等级 + 动态模式 |

**复用现有**（不要重写）：

| 需求 | 用什么 |
|---|---|
| 气液分离罐 | `Vessel`（已有动态、`AccumulationStream`） |
| 电解液循环泵 | `Pump`（已有曲线、变速） |
| 冷却器/板换 | `HeatExchanger` |
| 多级压缩（90→250 bar） | `Compressor` 串两级 |
| 液位/温度/压力指示 | `LevelGauge` / `AnalogGauge` |

**流程模板**（`PlatformFiles/Common/samples/`）：

- `PEM Electrolysis Plant.dwxmz`：参数取 Bzovsky 2024（1 MW / 182 cells / 1000 cm²）
- `Alkaline Electrolysis Plant.dwxmz`：参数取 Dinh 2021（3 MW / 16 bar）

拓扑参考 Bianchi & Bosio 2026（DOI `10.5281/zenodo.18410564`）：PEMEC → 阴极循环 → G/L 分离 → H2 纯化 → 两级压缩（90 bar → 250 bar）→ 储罐。

**验收**：

```sh
# 1. 两个模板能打开、能算、稳态结果合理
# 2. 动态模式能跑（H2 储罐压力随产氢上升）
# 3. 与文献对比：H2 纯度 99.9%（Bzovsky 2024 的结论）
```

---

## 7. Phase E：验证与发布

### T-5.1 闭环验证（虚拟 I/O）

**前置**：T-3.4 或 T-3.5 至少一个完成；T-1.4 完成

**验证矩阵**：

| 用例 | 链路 | 判据 |
|---|---|---|
| V-5.1.1 | Modbus 自环（无 OpenPLC） | 写入→模型→读出，往返正确，`Word/Scale` 无失真 |
| V-5.1.2 | Modbus + OpenPLC v4 Docker | 温度回路：PID 在 PLC 里，SP 阶跃 80→85 °C，超调 < 5%，稳态误差 < 0.5 K |
| V-5.1.3 | Modbus + OpenPLC v4 | 压力回路：阴极压力 SP 阶跃 20→30 bar |
| V-5.1.4 | Modbus + OpenPLC v4 | 液位回路：H2 分离罐液位 SP 阶跃 50→70% |
| V-5.1.5 | OPC UA + OpenPLC v4 | V-5.1.2 同用例，结果与 Modbus 版本对比（差异 < 2%） |
| V-5.1.6 | Modbus + `tc` 注入 20ms 延迟 + 1% 丢包 | 闭环仍稳定，不振荡发散 |
| V-5.1.7 | Shadow 模式 | 内部 PID 与 PLC 同时算，偏差曲线可导出，偏差 < 5% |
| V-5.1.8 | 联锁 | PLC 侧强制 H2inO2 = 5% → 模型跳车 → PLC 读到 Trip 位 |
| V-5.1.9 | 时序 | 记录"模型时间 vs 墙钟"偏差曲线，60 s 内偏差 < 2% |
| V-5.1.10 | 超时策略 | 人为让某步慢 3× → 按 `OverrunPolicy.Skip` 丢步，质量码降级，PLC 不崩 |

**必须产出的证据**：

- `tests/reports/closed-loop/` 下的时间序列 CSV 与曲线图
- `docs/verification.md`：每条用例的执行记录、截图、结论
- **明确写"仅虚拟 I/O 验证，未经真实 PLC 硬件在环"**

**验收**：

```sh
ls tests/reports/closed-loop/*.csv | wc -l    # 每条用例至少 1 个
grep -c "PASS" docs/verification.md           # 应 >= 10
```

---

### T-5.2 电解槽数值回归

**前置**：T-1.2、T-1.4

**新建** `tests/electrolyzers/`，4 组黄金数据（数据来源见 `docs/h2-electrolyzer-retrofit-analysis.md` §4.6）：

| 测试 | 数据来源 | 判据 |
|---|---|---|
| 极化曲线 | Bzovsky 2024 Table 4 + Fig 2；R9 电导率式 | `U_cell` 相对误差 < 2% |
| 部分负荷 / SEC | Bzovsky 2024 Fig 5；ICPEE 2025 KPI 表 | SEC 误差 < 3% |
| 稳态/动态热行为 | Dinh 2021（3 MW ALK） | 稳态温度 < 1 K；时间常数 < 10% |
| 气体纯度 | R2（4% LEL 曲线）；Bzovsky（99.9%） | 趋势一致 + LEL 交点位置误差 < 10% |

**注意**：现有 `DWSIM.Tests/Program.cs` 只跑"不抛异常"，**不校验数值**。需要新增数值断言（用 console assert，不必引入 NUnit；`DWSIM.Automation.Tests.CSharp` 已是 console 风格）。

**验收**：

```sh
# 4 个测试全部 PASS，且在 CI 或本地可一键跑
dotnet run --project DWSIM.Tests  # 或 mono DWSIM.Tests.exe
```

---

### T-6.1 版本与文档

**前置**：T-5.1、T-5.2

**操作**：

1. `bash scripts/version.sh minor` → `VERSION` 升到 `0.2.0`，同步所有 `AssemblyInfo.vb` 的 `AssemblyVersion`
2. `CHANGELOG.md` 写清：新增电解槽模型、动态支持、Modbus/OPC UA 桥、ControlArbiter、HeadlessDynamicsRunner
3. `docs/` 补：`io-map.md`、`verification.md`、`parameters/*.md`、`development.md`（含 macOS Rosetta 说明）
4. `AGENTS.md` 更新：构建命令、产物名、升版命令
5. 工作区 `PROJECTS.md` 登记 H2sim（**注意：目录名 `H2sim` 含大写，与工作区"全小写 ASCII"规约冲突 → 登记时说明为上游派生仓库的例外，或与用户确认是否改名为 `h2-sim`**）

---

### T-6.2 打包发布

**前置**：T-6.1、T-0.3

**产物**：

| 平台 | 产物 | 说明 |
|---|---|---|
| Windows | `release/windows/H2sim-<ver>-windows-x64.exe` | 单文件绿色版（资源内嵌或首次运行自解压） |
| macOS | `release/macos/H2sim-<ver>-macos-x64.dmg` | 内含 `.app`。**架构标 `x64` 因为走 Rosetta**（若将来实现 M2 再出 `arm64`） |

**Windows 关键**：

- 不要把 `PlatformFiles/Common/*` 散落一堆（DWSIM 现在是 `xcopy` 到输出目录，产物是几百个文件）→ 用单文件打包（Costura.Fody 或自解压），或至少压成一个 zip 里的清晰结构
- 若用自解压，解压目标要版本化（`%LOCALAPPDATA%\H2sim\<version>\`），避免升级冲突

**macOS 关键**：

- `Architectures=x86_64`
- 不打包 `ThermoCS/`
- 不打包 `DWSIM.ProFeatures.dll`
- `Info.plist` 写清 `LSMinimumSystemVersion`
- **未签名未公证** → 首次运行需右键打开，`docs/development.md` 写明

**验收**：

```sh
ls -la release/windows/H2sim-*-windows-x64.exe
ls -la release/macos/H2sim-*-macos-x64.dmg
# Windows：干净机器上双击能起、能算
# macOS：Apple Silicon 上（装了 Rosetta）能起、能算
# 两者都能连 OpenPLC v4（Modbus 或 OPC UA），跑通 V-5.1.2
```

---

## 8. 任务依赖总表（给 agent 排期用）

| 任务 | 前置 | 人周 | 涉及主要文件 |
|---|---|---|---|
| Gate 0 | — | 0.5 | （只读验证） |
| T-0.1 基建 | — | 0.5 | `VERSION`、`CHANGELOG.md`、`AGENTS.md`、`scripts/*`、`.gitignore` |
| T-0.2 平台收敛 | T-0.1 | 1.0 | `DWSIM.sln`、删 `DWSIM.UI.Desktop.GTK`、删 `PlatformFiles/Linux`+`Raspberry`、两个 `PostBuildEvent` |
| T-0.3 macOS/Arm | Gate0 V-3/V-4 | 2.0 | `ThermoCPropertyPackage.cs`、打包脚本、`AddPropPacks()` |
| T-3.1 HeadlessRunner | T-0.1、T-0.2 | 2.0 | 新建 `DWSIM.Automation.Dynamics/`、改两个 UI 的 `RunIntegrator` |
| T-3.2 SignalMap | T-3.1 | 1.0 | 新建 `DWSIM.H2sim.Io/SignalMap/*`、`Bridge/IPlantBridge.cs` |
| T-3.3 ControlArbiter+PID | T-3.1、T-3.2 | 1.5 | `Enums.vb`、`PIDController.vb`、新建 `Control/ControlArbiter.cs` |
| T-3.4 Modbus 从站 | T-3.1/2/3 | 1.5 | 新建 `Bridge/Modbus/*`、`docs/io-map.md` |
| T-3.5 OPC UA Client | T-3.2、T-3.3 | 2.5 | 新建 `Bridge/OpcUa/*` |
| T-1.1 UO 骨架 | T-0.1 | 1.0 | 新建 `UnitOperations/Electrolyzers/*` |
| T-1.2 电化学+KPI | T-1.1 | 2.5 | `ModelPEM_Springer.vb`、`ElectrolyzerCalculator.vb`、`Enums.vb`+`SystemsOfUnits.vb` |
| T-1.3 联锁 | T-1.2 | 0.5 | `ElectrolyzerUnitOpBase.vb` |
| T-1.4 动态支持 | T-1.3、T-3.1 | 1.0 | `ElectrolyzerUnitOpBase.vb`、两个电解槽类 |
| T-2.1 KOH/电导率 | T-0.1 | 2.0 | `electrolyte.xml`、`ModelAlkaline_SemiEmpirical.vb`、`docs/parameters/` |
| T-4.1 BoP | T-1.2 | 3.0 | `H2Purification.vb`、`HydrogenStorage.vb`、两个示例 flowsheet |
| T-5.1 闭环验证 | T-3.4 或 T-3.5；T-1.4 | 2.0 | `tests/reports/`、`docs/verification.md` |
| T-5.2 数值回归 | T-1.2、T-1.4 | 1.5 | `tests/electrolyzers/`、`DWSIM.Tests` |
| T-6.1 版本文档 | T-5.1、T-5.2 | 0.5 | `VERSION`、`CHANGELOG.md`、`docs/*`、`PROJECTS.md` |
| T-6.2 打包 | T-6.1、T-0.3 | 1.0 | `release/windows/`、`release/macos/`、打包脚本 |
| **合计** | | **26** | |

**关键路径**（最长链）：`Gate0 → T-0.1 → T-0.2 → T-3.1 → T-3.2 → T-3.3 → T-3.4 → T-5.1 → T-6.1 → T-6.2` = 0.5+0.5+1+2+1+1.5+1.5+2+0.5+1 = **11.5 人周**

Phase C（T-1.x + T-2.1，共 7 人周）与关键路径可并行，但 T-5.1 需要 T-1.4 → 实际总工期约 **13–16 人周**（单人）。

---

## 9. 验收检查清单（每个里程碑过一遍）

### M1（最小闭环）通过标准

- [ ] `scripts/build.sh` 全绿
- [ ] `Automation3` 能跑 `DynamicsRunner`，无 UI 依赖（`grep -i "Windows.Forms\|Eto" DWSIM.Automation.Dynamics/*` 为空）
- [ ] 6 个现有动态样例回归通过（误差 < 1e-6）
- [ ] SignalMap 序列化往返正确，校验器能拦住所有冲突
- [ ] PID 无扰切换无跳变
- [ ] Modbus 从站能被 NModbus client 读到结构化数据
- [ ] 质量码在模型故障时正确降级

### M2（模型到位）通过标准

- [ ] 极化曲线误差 < 2%
- [ ] SEC 误差 < 3%
- [ ] 动态热行为与 Dinh 2021 对比通过
- [ ] 联锁锁存与复位正确
- [ ] 质量/能量守恒 < 1e-8

### M3（功能完整）通过标准

- [ ] OPC UA 能连 OpenPLC v4，权限探测正确，断线重连可用
- [ ] 两个 BoP 模板能算
- [ ] macOS 在 Apple Silicon 上能起、能算、ThermoC 优雅禁用

### M4（可交付）通过标准

- [ ] V-5.1.1 ~ V-5.1.10 全部 PASS，证据齐全
- [ ] `VERSION` / 产物名 / `CHANGELOG` 三者一致
- [ ] `release/` 下两个产物在干净机器上可用
- [ ] `scripts/clean.sh --dry-run` 无残留中间产物
- [ ] `PROJECTS.md` 已登记

---

## 10. Agent 执行守则（硬性）

1. **每个任务卡开始前，先读它引用的文档与代码位置**。不要凭记忆实现。
2. **遇到 Gate 0 未通过或任务卡的前置未满足，停下来报告，不要硬推**。
3. **不要跨任务卡做"顺手优化"**。看到别的问题，记在 `docs/tech-debt.md`，不在当前 commit 里改。
4. **回归优先于新功能**。T-3.1 的 6 个动态样例回归是硬门槛；任何让它们变的行为都是 bug。
5. **不要引入新的 NuGet 包**，除非任务卡明确指定。引入时必须检查是否有 `net462` 或 `netstandard2.0` 资产。
6. **不要修改 `WaterElectrolyzer.vb` 的计算逻辑**。要修它的问题（如 `GetPropertyUnit` 的 `Case Else → 0.0`）单独开任务卡。
7. **不要修改 `CleanEnergyUnitOpBase.IsSource = True`**（那是给风机/光伏的）。电解槽新基类自己覆盖为 `False`。
8. **不要在通讯线程里调 `ISimulationObject.SetPropertyValue`**。
9. **不要在 `Enums.vb` 的枚举中间插入值**，只能在末尾追加。
10. **macOS 相关改动必须实测**，不能只靠"理论上应该可以"。

---

## 11. 附：常用精确文件路径索引

| 用途 | 路径 |
|---|---|
| 电解槽 legacy | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/WaterElectrolyzer.vb`（569 行，`Calculate()` 在 L394–545） |
| CleanEnergy 基类 | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/CleanEnergyUnitOpBase.vb`（`IsSource=True` 在 L14） |
| PEMFC 参数范式 | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/PEMFuelCellUnitOpBase.vb` L31–72 |
| 动态 UO 样板 | `DWSIM.UnitOperations/UnitOperations/Vessel.vb` L35（`SupportsDynamicMode`）/L135（`CreateDynamicProperties`）/L202（`RunDynamicModel`） |
| 动态契约 | `DWSIM.Interfaces/ISimulationObject.vb` L31/L45/L184；默认实现在 `DWSIM.SharedClasses/BaseClass/SimulationObjectBaseClasses.vb` L121（False）、L357（抛异常） |
| 计算顺序 | `DWSIM.FlowsheetSolver/FlowsheetSolver.vb` L1030（`IsSource` → `lists(0)`）、L1091–1103（`SupportsDynamicMode` 批处理） |
| 动态主循环 | `DWSIM/Forms/FlowsheetComponents/FormDynamicsIntegratorControl.vb` L265–660（WinForms）、`DWSIM.UI.Desktop.Editors/Dynamics/DynamicsIntegratorControls.cs`（Eto） |
| 事件处理 | 同上 Form L155（`ProcessEvents`）/L187（`ProcessCEMatrix`） |
| PID | `DWSIM.UnitOperations/Controllers/PIDController.vb` L70–130（参数）、L703（`UpdateVars`）、L722–794（`Calculate`） |
| 单位体系 | `DWSIM.Interfaces/IUnitsOfMeasure.vb`、`DWSIM.Interfaces/Enums.vb` L154–228（`UnitOfMeasure`，末尾 `specificpower = 71`）、`DWSIM.SharedClasses/UnitsOfMeasure/SystemsOfUnits.vb`（`SI` L500、`GetUnitType` L295、`GetCurrentUnits` L401） |
| 枚举 | `DWSIM.Interfaces/Enums.vb`：`ObjectType` L669、`SimulationObjectClass` L495（`Electrolyzers = 18` 在 L515）、`DimensionName` L626、`UnitOfMeasure` L154 |
| 外部 UO 发现 | `DWSIM.SharedClasses/Misc/Utilities.vb` L502–550（`LoadAdditionalUnitOperations`，扫 `unitops/` 与 `extenders/*Extensions*.dll`）、`DWSIM/Forms/MainWindow/FormMain.vb` L936 |
| 外部 UO 画布注册 | `DWSIM.FlowsheetBase/FlowsheetBase.vb` L1008（`ObjectType.External`） |
| 物性包列表 | `DWSIM/Forms/MainWindow/FormMain.vb` `AddPropPacks()`、`DWSIM.FlowsheetBase/FlowsheetBase.vb` L3608 附近 |
| ThermoC | `DWSIM.Thermodynamics.ThermoC/ThermoCPropertyPackage.cs` L93（构造，含 32/64 位检查 L97–112） |
| 化合物库 | `DWSIM.Thermodynamics/Assets/Databases/electrolyte.xml`（84 物种，有 `Hydroxide`/`Potassium Ion`/`Sodium Hydroxide`，缺 `Potassium Hydroxide`） |
| Henry | `DWSIM.Thermodynamics/Assets/henry_constants.csv`；用法见 `ElectrolyteIdeal.vb` L573 |
| 自动化 | `DWSIM.Automation/Automation.cs`（`Automation3` L244）、`DWSIM.Automation/Flowsheet2.cs` L23（无 UI 的 `FlowsheetBase` 实现） |
| 测试 | `DWSIM.Tests/Program.cs`（只跑通不校验）、`DWSIM.Automation.Tests.CSharp/*.cs` |
| 官方示例 | `PlatformFiles/Common/samples/Water Electrolyzer.dwxmz`、`Dynamic Simulation - *.dwxmz`（6 个） |
| 构建事件 | `DWSIM/DWSIM.vbproj` `PostBuildEvent`（约 L3872 起）、`DWSIM.UI.Desktop/DWSIM.UI.Desktop.csproj`（约 L409 起） |
| NuGet | `.nuget/NuGet.targets`（10+ 工程 import）、各工程 `packages.config` |
