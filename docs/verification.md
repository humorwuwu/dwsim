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

## 本地 macOS（Mono 6.12，x86_64 / Rosetta）

- `DWSIM.H2sim.Io.Tests`：**PASS**。覆盖 SignalMap 序列化/校验、缩放方向、NModbus 真实 TCP 自环。
- 全量 `DWSIM.Automation.Dynamics.Tests` 的本地构建被 `DWSIM.Controls.PythonConsoleControl`（WPF）挡住；Mono 没有 WPF，只能改用 CI 产物运行。
- GUI 与 Rosetta 启动仍属于 Gate 0 V-3，尚未完成。曾尝试下载上一轮 Windows
  CI 产物，但 GitHub artifact 下载持续无数据，已中止。

## T-3.1 UI host 接线

- WinForms 与 Eto 动态窗体已改为调用 `DynamicsRunner`，各自提供 `IDynamicsHost` 实现；WinForms 的单步前进/后退仍走 legacy 循环。
- 编译验证：CI Release/Debug 通过。
- 动态样例冒烟：5 个样例在 headless runner 下跑过 10 s（CSTR、换热器、分离器压力/液位、水罐液位、水罐自定义控制器）；`Dynamic Simulation - PFR.dwxmz` 因保存步长大于 PFR 动态停留时间被跳过（样例自身的 `PFR-001` 限制）。
- 未验证：Windows 桌面上的暂停/续跑、单步、图表和历史记录行为；6 个动态样例的数值回归基准尚未采集。

## T-4.1 示例流程模板

- 生成器：`DWSIM.Automation.Dynamics.Tests --generate-samples`，物性包 Raoult's Law，水/H2/O2 三组分，电解槽 + 冷却水 + H2/O2 出口。
- 产物：`PlatformFiles/Common/samples/PEM Electrolysis Plant.dwxmz`、`Alkaline Electrolysis Plant.dwxmz`；每套含电解槽、冷却水、H2 纯化、储罐和 H2/O2/off-gas 出口。
- 额定栈：PEM 使用 900 kW 运行点，PEM 模型在 2.5 A/cm2 的最大可达功率为
  920.47 kW；ALK 使用 17600 cm2 单电池面积、300 片，0.4 A/cm2 时模型功率为
  3000.17 kW。模板额定点取 2950 kW，为不同物性包的数值差异保留余量；
  两个已提交模板的参数值已同步，回读计算由 CI 验证。
- 额定点修复后的完整 Windows CI：run `43818072975`，Debug/Release、GUI 启动、
  单文件打包、SignalMap、OpenPLC v4 Modbus/OPC UA 探针、动态测试和模板生成全部通过。
- 回归：`DWSIM.Automation.Dynamics.Tests` 的 `TestPlantSamples` 重新加载并计算两个已提交模板，断言无求解错误、功率与模板额定值相差 <3%，并检查串联电池电流定义。
- 储罐动态：60 s 动态测试要求储罐压力上升，CI run `43724380091` 通过。
- 加载链路修复：自动化加载前注册外部 UO；外部 UO 图形按保存的连接器数量重建真实连接点；连接恢复使用保存的输出索引。
- 未覆盖：G/L 分离和两级压缩尚未接入；完整质量/能量守恒及文献曲线回归仍未完成。

## T-5.2 电解槽数值回归

- 极化曲线：Bzovsky 2024 Fig. 2 数字化 18 点，60/70/80 degC、0.1–2.5 A/cm2，
  全部点相对误差 <2%。本地程序集运行探针结果 18/18 PASS。
- SEC：Bzovsky 2024 Fig. 6 数字化 15 点，100–900 kW，全部点相对误差 <3%。
  本地程序集运行探针结果 15/15 PASS。
- 热积分：4 个解析基准点，覆盖无冷却、无散热、60 s 和 600 s 大步长，
  内部子步结果全部 <1%。本地运行探针 4/4 PASS。
- 气体纯度：`crossover_coeff=1e-5` 时，2.5 A/cm2 的 H2-in-O2 为 0.00034%，
  0.02 A/cm2 为 5.08%，低载趋势和 4 vol% LEL 穿越成立。
- KOH 电导率：Gambou 2022 Equation 41 的 6 个 25/50/80 °C、20–35 wt% 点，
  相对误差 <0.1%；关联式范围外参数显式拒绝。
- 仍然待补：Dinh 2021 热时间常数原始曲线、R2 气体穿透数值曲线、ALK 极化/SEC
  黄金数据与完整质量/能量守恒。

## 待执行

| 用例 | 依赖 |
|---|---|
| V-5.1.7 ~ V-5.1.10 | Shadow、联锁、时序和超时策略 |
| T-5.2 剩余数值回归 | Dinh 2021 原始曲线、R2 曲线、ALK 黄金数据 |
| macOS 运行 | Gate 0 V-3 原生产物与 Rosetta 启动 |

生成证据时，时间序列 CSV 放 `tests/reports/closed-loop/`，本文件逐条记录结论。
