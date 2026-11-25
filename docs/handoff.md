# 交接文档（2026-11-23 晚）

给接手的 agent：先读 `AGENTS.md`，再读本文件。本文件只写「现在在哪、下一步做什么、别踩什么坑」。

## 0. 仓库与状态

- 仓库：`/Volumes/SSD_exit/code_go/H2sim`（外置 SSD；旧的 `/Users/wcx/ongoing/H2sim` 已不是工作副本）
- 分支：`h2sim/phase-a`
- 版本：`VERSION` = `0.4.0`，`CHANGELOG.md` 有对应的 0.4.0 节，所有 `AssemblyInfo` 已同步
- 今天的工作已提交并推送（`origin/h2sim/phase-a`）：
  - `T-0.3`：非 Windows 构建 + macOS 打包脚本
  - `T-4.1`：阴极闪蒸 + 两级压缩断言 + ALK SEC 回归
  - `release 0.3.0`：版本与 `AssemblyInfo`
  - `docs`：macOS V-3 失败记录与交接
  - `T-4.1`：外部 UO 描述写入图形，模板重新生成
  - `ci`：4.6.1 devpack 安装重试 + 安装包静默安装/启动冒烟
  - `T-1.2`：电解槽报告输出全部 KPI，纯度补 `%` 单位
  - `release 0.3.1`：版本与 `AssemblyInfo`
  - `macos`：GTK3 启动、自包含 bundle、Rosetta env/fontconfig 修复
  - `release 0.3.2`：版本与 `AssemblyInfo`
  - `T-1.4`：LyeLoop 冷却模式（effectiveness-NTU）
  - `release 0.4.0`：版本与 `AssemblyInfo`
- Windows 0.4.0 run `45525384510`：整轮 **success**，OpenPLC skipped；包在
  `release/windows/H2sim-0.4.0-windows-x64.exe`（195 MiB，SHA-256
  `c1b98a59…`）。旧版本的包也还在同目录，留作回退。
- macOS 0.4.0 run `45525384567`：整轮 **success**，44 个内置 GTK3 库，
  动力学测试 **PASS**。包在 `release/macos/H2sim-0.4.0-macos-x64.dmg`
  （179 MiB，SHA-256 `664da293…`），目标机只需 Mono 6.12（Apple Silicon 走
  Rosetta），不再需要单独安装 GTK3。启动器用
  `arch -x86_64 env DYLD_LIBRARY_PATH=...` 绕过 Rosetta 切换丢环境变量；
  GTK3 路径已在 0.3.2 上做过真实窗口验证
  （`assets/macos-gtk3-0.3.2-*.png`），0.4.0 沿用同一打包链。
- 本机 `mono` 跑 `DWSIM.Automation.Dynamics.Tests`，末行 **PASS**（约 7 分钟）。
  日志里的 ThermoC / ThermoCS `TypeLoadException` 是 i386 原生库，忽略，以末行 `PASS` 为准。

## 1. 当前方向（必须遵守）

1. **优先做 DWSIM 自身的功能和模型。**
2. **PLC / OpenPLC 全部暂停**（V-5.1.8–V-5.1.10）。V-5.1.8 的代码在，用环境变量
   `H2SIM_INTERLOCK_TEST` 关闭。不要修，不要打开。
3. macOS 本地跑通 `DWSIM.Automation.Dynamics.Tests` 已经完成。不要再为这件事改 WPF 方案。

## 2. 今天做完、并且在这台 Mac 上跑过的

| 改动 | 本机证据 |
|---|---|
| 非 Windows 能编过并跑 Dynamics 测试 | 末行 PASS。ALK 314.15 K / 8.13 kg/s，PEM 310.74 K / 5.80 kg/s，热中性闭合 0.0000% |
| 阴极气先冷到 40 °C，再由 `Vessel`（标签 `V-GL`）闪蒸 | ALK 水 0.278 mol/s 进、0.235 mol/s 凝出；PEM 0.0370 进、0.0314 凝出。干气标签 `H2-DRY`，凝液 `H2-COND` |
| ALK SEC 6 个锚点 | `alkaline SEC: 6 points, worst 2.11%` |
| 两级压缩断言 | 稳态 C-1/C-2 轴功 = 能量流 = 质量流量 × 焓升；动态只要求出口压力规格和正轴功 |
| 外部 UO 描述入库 | `AddObjectToSurface` 写入 `GraphicObject.Description`；模板加载时按描述匹配电解槽/纯化器/储罐，测试断言描述非空 |
| 厂模板重新生成 | `PlatformFiles/Common/samples/PEM Electrolysis Plant.dwxmz`、`Alkaline Electrolysis Plant.dwxmz` |

ALK SEC **不是论文数字化曲线**。它把已有的 30 wt% KOH / 80 °C 电压包络按法拉第定律换算：
`etaF_ref = 0.99` 在 `i_ref = 0.4 A/cm²`。容差与极化包络相同。有原始极化曲线后再一起替换
`alkaline_polarization_reference.csv` 和 `alkaline_sec_reference.csv`。

阴极闪蒸用的是现成的 `Cooler`（定温 313.15 K）和 `Vessel`，没有新单元操作。纯化器仍然把
残余水蒸气和氧去掉。生成器在 `Program.cs` 的 `AddCathodeFlash`。改生成器后必须重新生成两个 `.dwxmz`。

### 非 Windows 能编过的做法（不要改回方案 A/B）

方案 A（整份 WPF 桩）盖不住 `Program.cs` 里的 `Eto.Wpf`。方案 B（从 Automation 去掉
`DWSIM.UI.Desktop`）编不过，因为 `Automation` / `Automation2` 和测试用的 `Automation3`
在同一个程序集里。

现在的做法只在 `OS != Windows_NT` 生效，Windows 构建不变：

- `Directory.Build.targets` 在 `AssignProjectConfiguration` 之前去掉 WPF 工程和两个
  IronPython 控制台工程，并去掉 WPF 程序集引用。
- 同一个文件在 `SplitResourcesByCulture` 之前丢掉 `*.licx`。Mono 的 `lc.exe` 退出码是 1。
  不要用字符串替换 `CoreCompileDependsOn`，分号会把属性截断，VB/C# 工程会报 MSB4057。
- 编译符号 `H2SIM_NO_WPF`。C# 加在 `DefineConstants` 上。VB 读的是 `FinalDefineConstants`，
  必须直接改这个属性，改 `DefineConstants` 不会进 `vbc`。
- 符号包住：WPF 渲染分支、Dispatcher、`GeoCoordinateWatcher`、`Imports PythonConsoleControl`。

Windows 上 `H2SIM_NO_WPF` 不定义，这些分支仍参与编译。**这批改动还没有跑过 Windows CI。**
推上去之后第一件事就是看 CI。

## 3. 已在 HEAD 里、有 CI 证据的模型改动

| 改动 | 提交 |
|---|---|
| ALK 电压重新标定（0.4 A/cm² 约 1.76 V，高于热中性） | `caf5c93ab` |
| Faraday 效率 `i/(i+i_cross)`，每类电解槽自带 `i_ref` | `a79a74198` |
| 气体穿透随阴极压力和温度缩放 | `d4581b33e` |
| 穿透写入出口流股；`O2 in H2` / `H2 in O2` / `H2Purity` 从流股读回 | `b6919f04e`、`1b271185b`、`ec8035959` |
| 功率低于电堆最小负荷时报错，不再静默钳位 | `e5d3f6b7b` |
| 冷却水按 `CoolingDuty` 定容 | `450b95603`、`b19de7b12` |
| 储罐用物性包 `AUX_Z`，无物性包才回退 0.95 | `5173f354b` |
| 纯化器同时报 `Product Purity` 和 `Product Impurity` | `fa8cb48f5` |
| `Cooler` 动态模型接受定温 / 温变 / 出口汽化率 | `4b63854b4`、`c793d082e` |
| PFR 样例空隙率 0 → 0.4 | `a37fa406e` |
| `RunOptions.StepSeconds` 传给积分器 | `82f7374d0` |
| 动态样例数值基准 `tests/dynamic/samples_baseline.csv` | `T-3.1` |

已有回归：元素守恒 1e-16、热中性闭合 0.0000%、ALK 极化 6 点最差 2.1%、ALK SEC 6 点最差
2.11%、动态 6 样例基准（容差 1e-6，`H2SIM_WRITE_BASELINE=1` 重生成）、环境散热占比 1.59%
（只对照 Dinh 2021 的 2.6% 做量级）。

## 4. 裁决（别推翻）

1. **`Cooler.vb` 动态行为已改。** 定温、温变、出口汽化率按设定点算，不再抛异常。代价是配错
   定温冷却器时动态下会静默维持该温度。
2. **不加 shunt-current 占比。** 它和穿透损耗都正比于电流，拆开不改变 `FaradayEfficiency`。
3. **不编造 Dinh 2021 热时间常数曲线，也不编造 R2 气体穿透数值曲线。** 分析文档里只有定性结论。
4. ALK 极化和 ALK SEC 都是工业电压包络锚点，不是论文描点。文件头写了替换条件。
5. V-5.1.7 的 Shadow 判据用「同信息量偏差」（PLC 本步命令 vs 内部 PID 上一步输出）。原始闭环
   偏差另列，因为差值主要来自 Modbus 一步零阶保持。
6. 非 Windows 构建没有 WPF 渲染器、交互式 IronPython 控制台、定位按钮。这是测试能编过的代价，
   不是桌面发行方案。
7. **两级压缩保持出口压力模式。** 重新生成的模板里 `C-1` / `C-2` 的 `DeltaQ` 不是 0，能量流
   `W-C1` / `W-C2` 与之相同。ALK 71.69 kW / 37.24 kW，PEM 10.47 kW / 9.45 kW。出口压力是规格，
   轴功是结果。生成器在稳态之后不改成 `PowerRequired`。
8. **macOS 桌面方案 M1 已判失败，不要再试图用系统 Mono 启动 XamMac2。**
   GTK3 是当前运行时路径：包启动器用 mono 直接跑 `DWSIM.UI.Desktop.exe`，
   设置 `H2SIM_MAC_RENDERER=Gtk3` 和 `PANGOCAIRO_BACKEND=fontconfig`；XamMac2
   启动器不再进包。CI 探针和本机 Apple Silicon `open` 已经验证打包后的 app
   创建 `DWSIM Launcher` / 主窗口，截图在 `assets/macos-gtk3-0.3.2-*.png`。
   GTK3 需要 x86_64 的 `libgtk-3.0.dylib`（已内置）。

## 5. 下一步

按这个顺序，不要先做发布，也不要做 PLC。

1. **Windows CI 已绿。** 最近两轮 `45106985172`（HEAD `6bbded0c8`）和
   `45109230393`（HEAD `54f045bd5`，含 devpack 重试）都是整轮 success。
   若再改 `.github/workflows/`，token 必须带 `workflow` scope：

   ```bash
   gh auth refresh -h github.com -s workflow
   gh auth setup-git
   ```

   这台机器到 github.com 的 IPv4 路由会抖动，push 用：

   ```bash
   git -c http.version=HTTP/1.1 -c http.postBuffer=524288000 \
       -c http.curloptResolve=github.com:443:20.247.166.64 \
       -c url."https://github.com/".insteadOf="git@github.com:" \
       push origin h2sim/phase-a
   ```

   `20.247.166.64` 是当前 `dig github.com` 的地址；失效时重新查。纯文档 commit 可带
   `[skip ci]` 避免再跑一轮。

2. **Windows 安装包已按 0.3.1 构建并在 CI 里装过。** 最新包来自 CI run
   `45118882085` 的 `h2sim-windows-package` artifact：
   `release/windows/H2sim-0.3.1-windows-x64.exe`（195 MiB，SHA-256 `8c5bd493…`）。
   包内 3718 个文件，`DWSIM.exe` 的 `AssemblyVersion` 是 `0.3.1.0`。

   CI 的 **Smoke test the Windows package** 会 `/S` 静默安装到
   `%LOCALAPPDATA%\H2sim\0.3.1`、检查 `DWSIM.exe` 与 `samples/`、再启动并存活 20 s。
   还差一台干净的 Windows 机器做人工双击验证；`release/` 不进 git。

3. **Dinh 2021 热时间常数、R2 穿透曲线。** 仓库里没有可数字化的原始曲线。只有拿到曲线再做回归。
4. **LyeLoop 冷却模式已实现通用 effectiveness-NTU 模型**（`CoolingMode=1`、
   `LyeFlow`、`LyeCp`、`HXEffectiveness`），`WaterFlow` 仍是默认。R7 的
   8–12% 变流量收益缺原始曲线/拓扑，未标定。
5. **macOS 桌面走 GTK3（.NET 8 仍是备选）。** 本地编译、dmg 打包和 GitHub
   `build-macos` 已补全；包启动器已改为 mono + GTK3，GTK3 库/pixbuf loaders
   已打进 dmg（`H2SIM_BUNDLE_GTK3=1`）。CI 探针确认隐藏 `/usr/local/lib` 后
   打包 app 仍存活并创建 `DWSIM` 窗口（run `45319801481`）；本机 Apple Silicon
   用 `open` 启动 0.3.2 dmg，Launcher 和 `Version 0.3.2` 主窗口已截图
   （`assets/macos-gtk3-0.3.2-*.png`）。目标机只需要 **Mono 6.12**
   （x86_64；Apple Silicon 走 Rosetta）。完整菜单/绘图/计算交互仍待人工走查。
6. **`PROJECTS.md` 已登记。** 目录名 `H2sim` 保留大写，写在工作区「项目例外」里。

明确不做：

- V-5.1.8 / V-5.1.9 / V-5.1.10
- OPC UA `Basic256Sha256` + Username、断线重连、权限不足

`openplc-compiler` job 现在在 push 时跳过（`build-windows.yml` 里
`workflow_dispatch` 的 `openplc=true` 才跑），这样暂停 PLC 期间 Windows
release 不会被 fixture 编译失败拖红。

## 6. 在这台机器上验证

macOS，Mono 6.12（6.12.0.206，MSBuild 16.10.1，NuGet 6.6.2.1）。必须 `/m:1`；
`/m` 会在 `EnumProcesses` 处崩。

桌面程序（Eto UI）本机构建：

```bash
cd /Volumes/SSD_exit/code_go/H2sim
bash scripts/build-macos.sh               # Release -> DWSIM.UI.Desktop/bin/Release/
CONFIG=Debug bash scripts/build-macos.sh  # Debug
```

脚本只构建 `DWSIM.UI.Desktop` 依赖图，跳过 WPF 工程、`licenses.licx` 和 Windows
`xcopy` PostBuildEvent，复制 `PlatformFiles/Common` + `PlatformFiles/macOS`，再移除
`ThermoCS` / ThermoC / ProFeatures。**不要**在本机直接 `msbuild DWSIM.sln`：sln 里
有 WPF 工程。

动力学测试（一条命令）：

```bash
cd /Volumes/SSD_exit/code_go/H2sim
bash scripts/test-macos.sh
```

脚本内部会做下面这些步骤；排错时再手工执行：

```bash
cd /Volumes/SSD_exit/code_go/H2sim
FACADES=/Library/Frameworks/Mono.framework/Versions/6.12.0/lib/mono/4.7.2-api/Facades

msbuild DWSIM.Automation.Dynamics.Tests/DWSIM.Automation.Dynamics.Tests.csproj \
  /p:Configuration=Release /p:Platform=AnyCPU \
  /p:PostBuildEvent= /p:GenerateSerializationAssemblies=Off \
  /p:NetStandardFacadesPath=$FACADES /m:1

find DWSIM/bin/Release -maxdepth 1 \( -name '*.dll' -o -name '*.dylib' -o -name '*.config' \) \
  -exec cp -n {} DWSIM.Automation.Dynamics.Tests/bin/Release/ \;

mono DWSIM.Automation.Dynamics.Tests/bin/Release/DWSIM.Automation.Dynamics.Tests.exe
```

`DWSIM.Interfaces` 等工程的 `ProjectReference` 是 `Private=False`，不拷贝就会在运行时缺程序集。
期望末行 `PASS`，并看到 `alkaline SEC`、`flash`、`compression steady` / `compression dynamic` 几行。

重生成厂模板（改了 `AddCathodeFlash` 或电解槽参数之后）：

```bash
mono DWSIM.Automation.Dynamics.Tests/bin/Release/DWSIM.Automation.Dynamics.Tests.exe \
  --generate-samples "PlatformFiles/Common/samples"
```

Windows 上照 `AGENTS.md`：`nuget restore` 后 `scripts/build.ps1`。CI 测试的工作目录是
`DistPackages\Windows`。`tests/electrolyzers/*.csv` 和 `tests/dynamic/*.csv` 必须在测试工程里
`Link` + `CopyToOutputDirectory`，否则找不到文件时只打印 "no reference"，不会失败。

## 7. 别再踩的坑

1. 读真实日志。以前多次「通过」是因为分支被静默跳过。
2. `Automation3.CreateFlowsheet()` 返回 `Flowsheet2`（继承 `FlowsheetBase`）。经典 UI 的
   `FormFlowsheet` 另有一份 `DynamicsManager`。改动态代码时确认用的是哪一份。
3. 厂模板改生成器后必须重新生成并带上 `.dwxmz`，否则测试仍在跑旧流程。
4. 不要改 `WaterElectrolyzer.vb` 的计算。新单元操作走 `IExternalUnitOperation`，放在
   `DWSIM.UnitOperations/UnitOperations/Electrolyzers/`。
5. 不要在通讯线程里调 `ISimulationObject.SetPropertyValue`。
6. 公开文档和提交说明里不写真实厂站名、厂商投标原文。
7. `Eto.Forms 2.8.3` 的 `netstandard2.0/Eto.dll` 缺 `Eto.IO.SystemIcons/IHandler`；只有
   `Eto.Forms 2.8.2` 的 netstandard2.0 资产有。这个坑只在 macOS 的 XamMac2 路径上暴露。
