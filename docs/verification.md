# 验证记录

> 仅虚拟 I/O 验证，**未经真实 PLC 硬件在环**。

## 环境

- CI：GitHub Actions `windows-latest`，分支 `h2sim/phase-a`
- 构建：Debug/Release x64，全部通过
- 测试：`DWSIM.H2sim.Io.Tests`、`DWSIM.Automation.Dynamics.Tests`

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

## 本地 macOS（Mono 6.12，x86_64 / Rosetta）

- `DWSIM.H2sim.Io.Tests`：**PASS**。覆盖 SignalMap 序列化/校验、缩放方向、NModbus 真实 TCP 自环。
- 全量 `DWSIM.Automation.Dynamics.Tests` 的本地构建被 `DWSIM.Controls.PythonConsoleControl`（WPF）挡住；Mono 没有 WPF，只能改用 CI 产物运行。
- GUI 与 Rosetta 启动仍属于 Gate 0 V-3，尚未完成。

## T-3.1 UI host 接线

- WinForms 与 Eto 动态窗体已改为调用 `DynamicsRunner`，各自提供 `IDynamicsHost` 实现；WinForms 的单步前进/后退仍走 legacy 循环。
- 编译验证：CI Release/Debug 通过。
- 动态样例冒烟：5 个样例在 headless runner 下跑过 10 s（CSTR、换热器、分离器压力/液位、水罐液位、水罐自定义控制器）；`Dynamic Simulation - PFR.dwxmz` 因保存步长大于 PFR 动态停留时间被跳过（样例自身的 `PFR-001` 限制）。
- 未验证：Windows 桌面上的暂停/续跑、单步、图表和历史记录行为；6 个动态样例的数值回归基准尚未采集。

## T-4.1 示例流程模板

- 生成器：`DWSIM.Automation.Dynamics.Tests --generate-samples`，物性包 Raoult's Law，水/H2/O2 三组分，电解槽 + 冷却水 + H2/O2 出口。
- 产物：`PlatformFiles/Common/samples/PEM Electrolysis Plant.dwxmz`、`Alkaline Electrolysis Plant.dwxmz`；每套含电解槽、冷却水、H2 纯化、储罐和 H2/O2/off-gas 出口。CI run `43722436127` 生成并上传；回读/计算由下一 CI run 验证。
- 计算结果：PEM 567.82 kW / H2 纯度 99.9998%；ALK 170.43 kW / 99.9953%；纯化/储罐链在生成器内计算通过。
- 回归：`DWSIM.Automation.Dynamics.Tests` 的 `TestPlantSamples` 重新加载并计算两个已提交模板，断言无求解错误且电解槽功率 > 0。
- 加载链路修复：自动化加载前注册外部 UO；外部 UO 图形按保存的连接器数量重建真实连接点；连接恢复使用保存的输出索引。
- 未覆盖：模板额定功率与 UO 默认栈尺寸/标定不匹配（请求 1000/3000 kW，实际 567.8/170.4 kW）；G/L 分离和两级压缩尚未接入；储罐动态模式（计划验收 2）未验证；完整质量/能量守恒回归仍待 T-5.2 黄金数据。

## 待执行

| 用例 | 依赖 |
|---|---|
| V-5.1.2 ~ V-5.1.10 | OpenPLC v4 Docker + 真实 Modbus/OPC UA 主站；本会话环境不可用 |
| T-5.2 数值回归 | Bzovsky 2024 图表数据、Dinh 2021 数据 |
| macOS 运行 | Gate 0 V-3（x86_64 Mono） |

生成证据时，时间序列 CSV 放 `tests/reports/closed-loop/`，本文件逐条记录结论。