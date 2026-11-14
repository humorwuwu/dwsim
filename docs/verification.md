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

## 待执行

| 用例 | 依赖 |
|---|---|
| V-5.1.2 ~ V-5.1.10 | OpenPLC v4 Docker + 真实 Modbus/OPC UA 主站；本会话环境不可用 |
| T-5.2 数值回归 | Bzovsky 2024 图表数据、Dinh 2021 数据 |
| macOS 运行 | Gate 0 V-3（x86_64 Mono） |

生成证据时，时间序列 CSV 放 `tests/reports/closed-loop/`，本文件逐条记录结论。