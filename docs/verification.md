# 验证记录

> 仅虚拟 I/O 验证，**未经真实 PLC 硬件在环**。

## 环境

- CI：GitHub Actions `windows-latest`，分支 `h2sim/phase-a`
- 构建：Debug/Release x64，全部通过
- 测试：`DWSIM.H2sim.Io.Tests`、`DWSIM.Automation.Dynamics.Tests`，以及
  OpenPLC v4 官方 Modbus/OPC UA 插件探针

## V-5.1.1 Modbus 自环（无 OpenPLC）

- 流程：加载 `Dynamic Simulation - Water Tank Level Control.dwxmz` → 启动 `ModbusTcpServerBridge` → NModbus 客户端写 holding register 0（SP=1.8 m，Scale 1000）→ 用 `DynamicsRunner` + `BridgeDynamicsHost` 跑 2 步 → 客户端读 input register 0（液位）。
- 判据：
  - 步数 == 2
  - PLC 写入的 SP 到达 PID（`AdjustValue == 1.8`）
  - 读回的 Int16 原始值与模型液位 × 1000 相差 ≤ 2 mm
- 结果：**PASS**（CI run `43653329916`，dynamics smoke test）。
- 备注：该用例在 2026-11-15 发现并修复了 `ModbusScaler` 缩放方向反了的问题（commit `0194a45e6`）。

## V-3.5 OPC UA 客户端（mock server）

- 流程：`tests/opcua/mock_server.py`（asyncua）暴露 `ns=2;s=P_set` / `s=Out`；H2sim 用 `OpcUaClientBridge`（None / Anonymous）订阅 `P_set`，跑 2 步，把 PID 的 `OutputAbs` 写到 `Out`；再用第二个会话读回。
- 判据：读回值 ≈ 最终 `OutputAbs`（差 < 0.1），步数 == 2，PLC 写入的 SP 到达 PID。
- 结果：**PASS**（CI run `43658987888`）。
- 未覆盖：`Basic256Sha256` + Username、OpenPLC v4、断线重连、权限不足场景。

## V-5.1.1b OpenPLC v4 `modbus_master` 插件

- 流程：H2sim 侧启动 NModbus TCP 从站快照，FC4 输入寄存器 0 为 `853`；
  OpenPLC Runtime v4 官方 `modbus_master` 插件按 FC4 读取到 `%IW0`，并把
  `%QW0=1800` 按 FC16 写回 holding register 0。
- 固定上游：`autonomy-logic/openplc-runtime` commit `9fb5e1a`，
  `pymodbus==3.11.2`。
- 判据：插件读取值到达 `%IW0`，H2sim 收到 FC16 写入值 `1800`，插件线程正常停止。
- 结果：本地由测试探针验证通过；CI 步骤已加入 `build-windows.yml`。
- 未覆盖：完整 OpenPLC Runtime PLC 扫描程序、温度/压力/液位 PLC PID 回路。

## V-3.5b OpenPLC v4 OPC UA server 插件

- 流程：用 OpenPLC Runtime v4 官方 `opcua/server.py` 启动仅 None/Anonymous
  的测试服务器，内存在线值 `PLC.H2.P_set=5.0`；H2sim
  `OpcUaSessionFactory` 读取 5.0，通过标准 Write 写入 6.5，再次读取确认。
- 固定上游：`autonomy-logic/openplc-runtime` commit `9fb5e1a`，
  `asyncua==1.1.8`。
- 判据：H2sim 能连接官方 server manager、解析字符串 NodeId、读取并写入 REAL。
- 结果：本地测试探针通过；CI 步骤已加入 `build-windows.yml`。
- 未覆盖：Basic256Sha256 + Username、错误角色拒绝、断线重连。

## OpenPLC 三回路 PLC 程序编译

- 项目源：`tests/openplc/h2sim-three-loop/`，包含温度、压力、液位三个
  PI 回路，100 ms 任务周期，分段执行 80→85 °C、20→30 bar、50→70% 设定值阶跃。
- Modbus master：官方 `modbus_master` 每 100 ms 读取 FC4 `%IW0..2`，
  并通过 FC16 写回 `%QW0..2`；模型故障哨兵 `-32768` 会复位积分项。
- 编译：OpenPLC Editor `4.3.5` headless CLI + STruC++ `0.8.0`，
  目标 `OpenPLC Runtime v4`；Runtime v4 源包、`image.conf` 和
  `conf/modbus_master.json` 均生成成功。
- 复现：`bash tests/openplc/compile_editor_fixture.sh`；AppImage 固定校验和。

## V-5.1.2–V-5.1.4 OpenPLC 三回路闭环

- Runtime：官方容器镜像
  `ghcr.io/autonomy-logic/openplc-runtime@sha256:378deec33b371eb09f8344e714d88b48efc7387f631f793371ba9f6a6e381c9f`。
- 流程：OpenPLC Editor 上传并编译项目 → Runtime v4 内 PLC 执行三回路 PI →
  `modbus_master` 每 100 ms 通过 FC4 读取 `%IW0..2`、通过 FC16 写入 `%QW0..2`
  → 确定性的温压液虚拟过程模型回写测量值。
- 温度：80→85 °C 阶跃，超调 -0.95%（未越过 85 °C），稳态误差 0.329 K。
- 压力：20→30 bar 阶跃，稳态误差 0.025 bar。
- 液位：50→70% 阶跃，稳态误差 0.174%。
- 结果：**PASS**（CI run `43832938483`，`openplc-compiler` job）。
- 证据：[`tests/reports/closed-loop/v5.1.2-4-three-loop.csv`](../tests/reports/closed-loop/v5.1.2-4-three-loop.csv)，
  记录 300 s、约 2 Hz 的时间序列。温度输入和模拟执行器采用 0.01 °C / 0.01%
  分辨率，消除了 0.1 °C 量化造成的闭环死区。

## V-5.1.5 Modbus / OPC UA 三回路对照

- H2sim 作为 OPC UA client 连接 OpenPLC v4 内置 server，写三个过程量、读取
  三个控制输出；OPC UA 测试项目由 OpenPLC Editor 4.3.5 官方编译链生成。
- PLC 周期计数作为测试 epoch 在 OPC UA 客户端连接时清零，使两套协议在相同
  的 600/1200/1800 拍设定值序列上运行。
- OPC UA 温度峰值 `84.951 °C`、稳态 `84.659 °C`；压力 `29.977 bar`；
  液位 `69.843%`。
- 相对 Modbus：温度峰值差 `0.0011 K`（`0.022%`）、温度稳态差 `0.0122 K`
  （`0.244%`）、压力差 `0.0013 bar`（`0.013%`）、液位差 `0.0170%`
  （`0.085%`），全部 <2%。
- 结果：**PASS**（CI run `43832938483`，`openplc-compiler` job）。
- 证据：[`tests/reports/closed-loop/v5.1.5-three-loop-opcua.csv`](../tests/reports/closed-loop/v5.1.5-three-loop-opcua.csv)。
- 边界：仍是独立虚拟过程模型，未接入 `DWSIM.Automation.Dynamics`；OPC UA
  Anonymous/engineer 仅用于隔离测试，正式部署需收紧为用户名或证书权限。

## V-5.1.6 Modbus 延迟 + 丢包注入

- 流程：与 V-5.1.2–V-5.1.4 同一套三回路 OpenPLC Runtime v4 台架（PLC 每 100 ms 经
  FC4 读 `%IW0..2`、FC16 写 `%QW0..2`），在虚拟厂侧的传感器输入寄存器链路上注入
  20 ms 延迟与 1% 丢包（`tests/openplc/impairment.py`，固定随机种子）。
- 判据：与 V-5.1.2–4 相同（温度超调 < 5%、温度稳态误差 < 0.5 K、压力 < 0.5 bar、
  液位 < 1%），且不得出现振荡发散。
- 实测：链路丢帧 28 帧（约 0.9%）；温度峰值 84.938 °C、超调 -1.25%、稳态 84.645 °C、
  误差 0.355 K；压力稳态 29.973 bar、误差 0.027 bar；液位稳态 69.823%、误差 0.177%。
  与未注入链路（峰值 84.954 °C / 误差 0.330 K、压力 0.025 bar、液位 0.178%）相比
  偏差很小，闭环仍稳定。
- 结果：**PASS**（CI run `44910799357`，`openplc-compiler` job）。
- 证据：[`tests/reports/closed-loop/v5.1.6-impaired-three-loop.csv`](../tests/reports/closed-loop/v5.1.6-impaired-three-loop.csv)。
- 边界：注入在 Python 虚拟厂侧的输入寄存器链路上完成，没有使用 OS 级 `tc netem`
  （Windows CI 无 `tc`，Linux 台架跑在 host network 容器里、netem 需要额外权限），
  因此覆盖的是 I/O 级时延与丢包，不含真实 TCP 重传行为。链路侧仍是独立虚拟过程
  模型，未接入 DWSIM 动态模型。

## V-5.1.7 Shadow 模式偏差

- 流程：加载水罐液位样例，PID 所有权切到 `Shadow`；一个独立 PI（故意 3% 增益失配，
  `ShadowPlc`）经 Modbus 从站桥每步写执行器保持寄存器，模型内部 PID 同时计算但不写 MV，
  偏差逐步记录并导出 CSV。过程值与设定值都经输入寄存器零阶保持下发给 PLC，与真实
  Modbus 扫描一致；第 30 s 有一次 0.05 m 设定值阶跃（在发布之后施加，PLC 下一拍才看到）。
- 判据：同信息量偏差（PLC 本步命令 vs 内部 PID 上一步输出，二者看到的过程值/设定值相同）
  最大 < 5% 阀位行程；另外自检「回路确实动过」（内部输出行程 > 1%）。
- 实测：内部输出由 8.42% 爬到 100%，外部由 1.17% 爬到 100%；同信息量偏差最大
  **3.56%**（第 35 s），首步 1.17%；原始闭环偏差最大 14.63% —— 这是 Modbus 零阶保持造成的
  一步传输滞后（该回路每步输出爬升约 12%），不是控制律不一致，CSV 两列都保留。
- 结果：**PASS**（CI run `44966049175`，`Dynamics headless smoke test`）。
- 证据：[`tests/reports/closed-loop/v5.1.7-shadow-deviation.csv`](../tests/reports/closed-loop/v5.1.7-shadow-deviation.csv)。
- 边界：仍是虚拟 I/O；PLC 侧是测试进程内的独立控制律，不是 OpenPLC 程序；台架自行建立
  工况（样例保存的初始状态是空罐，会把排液阀钉在限幅上），未接入真实 PLC。

## 本地 macOS（Mono 6.12，x86_64 / Rosetta）

- `DWSIM.H2sim.Io.Tests`：**PASS**。覆盖 SignalMap 序列化/校验、缩放方向、NModbus 真实 TCP 自环。
- `DWSIM.Automation.Dynamics.Tests`：本机编过并跑到末行 **PASS**（约 7 分钟）。
  `Directory.Build.targets` 在非 Windows 构建里跳过 WPF 渲染器和两个 WPF Python
  控制台；`licenses.licx` 也跳过。日志里的 ThermoC/ThermoCS `TypeLoadException`
  来自 i386 原生库，可忽略，以末行 `PASS` 为准。
- 2026-11-23 复跑：`PASS`。ALK 314.15 K / 8.13 kg/s，PEM 310.74 K / 5.80 kg/s，
  `compression dynamic` 两行分别为 C-1 10.46 kW / C-2 9.45 kW（PEM）。

## macOS 本地构建工具链（2026-11-23）

- 环境：Mono `6.12.0.206`、MSBuild `16.10.1`、NuGet `6.6.2.1`，Apple Silicon。
- `bash scripts/build-macos.sh`（Release）和 `CONFIG=Debug bash scripts/build-macos.sh`
  均 **BUILD OK**，0 错误。`scripts/build.sh` 在 Darwin 上自动转给 `build-macos.sh`。
- 产物：`DWSIM.UI.Desktop/bin/<Config>/DWSIM.UI.Desktop.exe`。`PlatformFiles/Common`
  与 `PlatformFiles/macOS` 已复制到输出目录；`ThermoCS/`、ThermoC 程序集和
  `DWSIM.ProFeatures.dll` 已移除。
- 完整 `DWSIM.sln` 不在本机构建：sln 含 WPF 渲染器和两个 WPF Python 控制台，
  Mono 没有 WPF。脚本只构建 `DWSIM.UI.Desktop` 的依赖图（AnyCPU），并跳过
  `licenses.licx` 和 Windows `xcopy` PostBuildEvent。
- 工具链补全不等于窗口能启动：Gate 0 V-3 仍失败，见下节。
- 打包：`bash scripts/package-macos.sh` 成功产出
  `release/macos/H2sim-0.3.1-macos-x64.dmg`（168 MB，SHA-256 `bf2718a0…`）。
  包内有 `DWSIM.UI.Desktop.exe`、`Xamarin.Mac.dll` 和两个厂模板；不含
  `ThermoCS/`、ThermoC、ProFeatures。**仍然不能启动**，不要当发行物。

## Gate 0 V-3 macOS 桌面启动（2026-11-23）

- 本机：macOS 27.2.1（Apple Silicon），Mono Framework 6.12 已装。
- 产物：`release/macos/H2sim-0.3.0-macos-x64.dmg`，启动器为 x86_64，`hdiutil attach` 正常。
- 直接运行 `H2sim.app/Contents/MacOS/H2sim`：
  - **第一次失败**：`Eto.XamMac2 2.8.2` 初始化时找不到
    `Eto.IO.SystemIcons/IHandler`。`Eto.Forms 2.8.3` 的 `netstandard2.0/Eto.dll`
    没有这个嵌套类型（`Eto.Forms 2.8.2` 的同名程序集有）。把包内 `Eto.dll`
    换成 2.8.2 后越过此点。
  - **第二次失败**：`Xamarin.Mac 4.2.0.19` 的 `ObjCRuntime.Runtime.Initialize`
    调用 `System.Threading._ThreadPoolWaitCallback.SetDispatcher`。Mono 6.12 MDK
    和 Eto 的 `mono-6.12.0-osx-10.9-x64-eto` 运行时都没有这个内部方法；它属于
    Xamarin.Mac 自带的 Mono 运行时。本机没有 `/Library/Frameworks/Xamarin.Mac.framework`。
- 结果：**FAIL**。方案 M1（XamMac2 + 系统 Mono）不成立。dmg 只能证明打包流程，
  不能启动窗口；`release/macos/` 里的 0.3.0 产物不是可用发行版。
- 未做：方案 M2（GTK3）需要 x86_64 GTK3。Homebrew 在 Apple Silicon 上装的是
  arm64（已确认 `/opt/homebrew/lib/libgtk-3.0.dylib` 为 arm64），x86_64 的 Mono
  进程加载不了；要另装 Rosetta 前缀（`/usr/local`）或 gtk-osx 的 x86_64 构建。
  .NET 8 + `Eto.Platform.macOS` 是另一条路，但会牵动全仓 target framework。

## T-3.1 UI host 接线

- WinForms 与 Eto 动态窗体已改为调用 `DynamicsRunner`，各自提供 `IDynamicsHost` 实现；WinForms 的单步前进/后退仍走 legacy 循环。
- 编译验证：CI Release/Debug 通过。
- 动态样例冒烟：6 个样例全部在 headless runner 下跑过 10 s（CSTR、换热器、分离器压力/液位、水罐液位、水罐自定义控制器、PFR）。PFR 样例原先的 `CatalystVoidFraction=0` 让它停留时间为 0，已修正样例数据并以 0.1 s 步长运行。
- 未验证：Windows 桌面上的暂停/续跑、单步、图表和历史记录行为；6 个动态样例的数值回归基准尚未采集。

## T-4.1 示例流程模板

- 生成器：`DWSIM.Automation.Dynamics.Tests --generate-samples`，物性包 Raoult's Law，水/H2/O2 三组分，电解槽 + 冷却水 + H2/O2 出口。
- 产物：`PlatformFiles/Common/samples/PEM Electrolysis Plant.dwxmz`、`Alkaline Electrolysis Plant.dwxmz`；每套含电解槽、冷却水、阴极闪蒸、H2 纯化、两级压缩、储罐和 H2/O2/off-gas 出口。
- 额定栈：PEM 使用 900 kW 运行点，PEM 模型在 2.5 A/cm2 的最大可达功率为
  920.47 kW；ALK 使用 17600 cm2 单电池面积、300 片，0.4 A/cm2 时模型功率为
  3000.17 kW。模板额定点取 2950 kW，为不同物性包的数值差异保留余量；
  两个已提交模板的参数值已同步，回读计算由 CI 验证。
- 额定点修复后的完整 Windows CI：run `43818072975`，Debug/Release、GUI 启动、
  单文件打包、SignalMap、OpenPLC v4 Modbus/OPC UA 探针、动态测试和模板生成全部通过。
- 回归：`DWSIM.Automation.Dynamics.Tests` 的 `TestPlantSamples` 重新加载并计算两个已提交模板，断言无求解错误、功率与模板额定值相差 <3%，并检查串联电池电流定义。
- 储罐动态：60 s 动态测试要求储罐压力上升，CI run `43724380091` 通过。
- 加载链路修复：自动化加载前注册外部 UO；外部 UO 图形按保存的连接器数量重建真实连接点；连接恢复使用保存的输出索引；`AddObjectToSurface` 现在同时写入 `GraphicObject.Description`，模板加载时按描述匹配到电解槽、纯化器和储罐。
- 两级压缩：出口压力模式，75% 等熵，级间冷却 40 °C。已保存模板的轴功与能量流一致，且等于质量流量乘以焓升。ALK `C-1`/`C-2` 为 71.69 / 37.24 kW（15.5→90→250 bar），PEM 为 10.47 / 9.45 kW（29.5→90→250 bar）。级间分离罐在当前纯度下无凝液。
- 未覆盖：Dinh 2021 热时间常数与 R2 气体穿透的原始曲线回归。

## T-1.2 电解槽 KPI 报告（0.3.1）

- `GetReport` 现在输出 `GetProperties` 里的全部 KPI：电流密度、可逆/热中性电压、
  三种过电位、Faraday 效率、H2 纯度、两侧穿透、比能耗、HHV/LHV 效率、冷却功率、
  堆温和四个联锁位。
- `GetPropertyUnit` 给 `H2 Purity` / `H2 in O2` / `O2 in H2` 返回 `%`；
  Faraday 效率和 HHV/LHV 效率保持分数（不加 `%`）。
- 本地 `DWSIM.Automation.Dynamics.Tests` 完整跑过，末行 **PASS**。

## T-5.2 电解槽数值回归

- 极化曲线：Bzovsky 2024 Fig. 2 数字化 18 点，60/70/80 degC、0.1–2.5 A/cm2，
  全部点相对误差 <2%。本地程序集运行探针结果 18/18 PASS。
- ALK 极化：30 wt% KOH / 80 degC 工业电压包络 6 点（0.1–0.6 A/cm2，1.53–1.92 V），
  走生产模型的默认参数，最差 2.1%，且断言设计点电压高于热中性电压。
- SEC：Bzovsky 2024 Fig. 6 数字化 15 点，100–900 kW，全部点相对误差 <3%。
  本地程序集运行探针结果 15/15 PASS。
- ALK SEC：同一 30 wt% KOH / 80 °C 电压包络按 Faraday 定律换算的 6 点
  （`etaF_ref=0.99` 在 `i_ref=0.4 A/cm2`），容差与极化包络相同。不是论文数字化表。
- 热积分：4 个解析基准点，覆盖无冷却、无散热、60 s 和 600 s 大步长，
  内部子步结果全部 <1%。本地运行探针 4/4 PASS。
- 气体纯度：`crossover_coeff=1e-5` 时，2.5 A/cm2 的 H2-in-O2 为 0.00034%，
  0.02 A/cm2 为 5.08%，低载趋势和 4 vol% LEL 穿越成立。
- KOH 电导率：Gambou 2022 Equation 41 的 6 个 25/50/80 °C、20–35 wt% 点，
  相对误差 <0.1%；关联式范围外参数显式拒绝。
- 仍然待补：Dinh 2021 热时间常数原始曲线、R2 气体穿透数值曲线。

## T-3.1 动态样例数值回归基准

- 基准：`tests/dynamic/samples_baseline.csv`，6 个动态样例各跑 10 s 后所有物料流股的总焓。
- 判据：相对误差 < 1e-6；`H2SIM_WRITE_BASELINE=1` 时改写基准。
- 实测：两次独立 CI 运行（run `44990929832`、`44993586506`）得到逐位相同的总焓，
  6 个样例全部与基准一致，无「no reference」告警。
- 结果：**PASS**（CI run `44993586506`）。
- 用途：以后任何求解器、单元操作或积分器的改动只要移动了这些总量，CI 立即报错；
  有意的模型改动用 `H2SIM_WRITE_BASELINE=1` 重新生成基准并一并提交。

## 厂模板动态运行（T-4.1）

- 流程：加载两个已提交的厂模板（含两级压缩 30→90→250 bar、中间冷却与分离罐），
  用 `DynamicsRunner` 跑 60 s，检查储罐压力上升、电堆温度、冷却水流量与跳车位。
- 实测（CI run `45004627361`）：
  - ALK：电堆 314.15 K、冷却水 8.13 kg/s、未跳车、储罐压力上升。
  - PEM：电堆 310.74 K、冷却水 5.80 kg/s、未跳车、储罐压力上升。
- 期间修复的三处缺陷：
  1. 已提交模板的动态配置为空（旧生成路径），导致动态测试一直静默跳过 → 重新生成。
  2. `Cooler` 的动态模型拒绝出口温度/温变/出口汽化率三种模式 → 改为按设定点计算。
  3. 动态热模型不从冷却水流取流量 → 现在读连接流的流量与进口温度。
- 结果：**PASS**。
- 阴极气液分离：饱和阴极气冷到 40 °C 后由 `V-GL` 闪蒸。本机实测冷凝掉约 85% 的水（ALK 0.235 / 0.278 mol/s，PEM 0.0314 / 0.0370 mol/s），干气再进纯化器。
- 边界：两级压缩保持出口压力模式（90 bar、250 bar）和 75% 等熵效率，级间冷却器是 40 °C 定温；储罐动态是集总模型。动态步之后只要求轴功仍为正、规格不被改掉。级间冷却器的动态持液会改写级间流股压力，所以动态断言不要求流股压力仍等于规格。

## 0.3.1 Windows CI（2026-11-23）

- Run `45118882085`，HEAD `2b58eb194`；整轮 **success**（Debug/Release、
  动态测试、模板生成、单文件打包、安装冒烟、OpenPLC 台架）。
- 修复：电解槽 `GetReport` 输出全部 KPI，纯度属性补 `%` 单位；新增报告测试。
- Windows 包：`release/windows/H2sim-0.3.1-windows-x64.exe`，CI 的
  `h2sim-windows-package` artifact，安装冒烟已通过。
  SHA-256：`8c5bd493a5720db4c071cad91b3dc9b6568e9c0f3879a10dc40d9198867a0573`。
  包内 3718 个文件，`DWSIM.exe` 的 `AssemblyVersion` 为 `0.3.1.0`。

## 0.3.0 Windows CI（2026-11-23）

- Run `45106985172`，HEAD `6bbded0c8`；整轮 **success**。
- Run `45113895354`，HEAD `6eb00b613`；整轮 **success**，新增打包安装冒烟。
- `build (Debug)`、`build (Release)` 均通过。Release 侧依次通过：还原/构建、
  启动冒烟、SignalMap 测试、OpenPLC v4 Modbus 探针、动态 headless 冒烟、
  两个厂模板生成、单文件打包。
- Windows 产物：`release/windows/H2sim-0.3.0-windows-x64.exe`（195 MiB，NSIS
  自解压），由 CI 的 `h2sim-windows-package` artifact 下载（run `45113895354`，
  HEAD `6eb00b613`）。
  SHA-256：`edacc8a504a6c62bb52f5f9bf8c2ee696430c39364ac0ea4a26a104663bde177`。
- 包内容抽查（7-Zip 26.06）：3718 个文件，解压约 869 MB；`DWSIM.exe` 的
  `AssemblyVersion` 为 `0.3.0.0`；Windows 包包含 `ThermoCS/` 和
  `DWSIM.ProFeatures.dll`（Windows 经典 UI 预期）。
- 包内两个厂模板与仓库 `PlatformFiles/Common/samples/` 字节一致：
  PEM `219661a9…`，ALK `bff1ba4b…`。
- CI 新增 **Smoke test the Windows package**（run `45113895354`）：`/S` 静默安装到
  `%LOCALAPPDATA%\H2sim\0.3.0`，检查 `DWSIM.exe` 与 `samples/`，再启动已安装的
  DWSIM 并确认存活 20 s。**PASS**。
- 同一轮 `openplc-compiler` job 也通过（三回路台架、V-5.1.6 延迟/丢包、
  OPC UA 对照）；PLC 相关工作按 2026-11-22 方向仍暂停。
- 同轮暴露并修复的 CI 问题：Chocolatey 源 503 会让
  `netfx-4.6.1-devpack` 安装静默失败，随后所有 4.6.1 工程报 MSB3644。
  `build-windows.yml` 已加三次重试；该改动单独提交、需要 `workflow` scope。

## 待执行

| 用例 | 依赖 |
|---|---|
| V-5.1.8 ~ V-5.1.10 | 联锁、时序和超时策略（PLC 相关，按 2026-11-22 方向暂停） |
| T-5.2 剩余数值回归 | Dinh 2021 原始曲线、R2 曲线 |
| macOS 运行 | Gate 0 V-3 原生产物与 Rosetta 启动 |

生成证据时，时间序列 CSV 放 `tests/reports/closed-loop/`，本文件逐条记录结论。
