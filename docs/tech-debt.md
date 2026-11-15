# 技术债

- `DWSIM.Simulate365` 仍参与构建：`DWSIM.SharedClassesCSharp` 和多个 WinForms 窗体直接引用它，不能从 sln 移除。`DWSIM.Apps.AzureServer` 不在 sln 里，无需处理。
- `DWSIM.Plugins.NaturalGas`、`DWSIM.Apps.TCPServer` 等工程的 `PostBuildEvent` 仍含 `DistPackages\Linux` 清理行（T-0.2 只清理了 `DWSIM` 与 `DWSIM.UI.Desktop`）。不影响 Windows 构建，下次动打包时一并删。
- 工程文件里的 `ReleaseLinux|*` / `wine*` PropertyGroup 保留，但已没有 solution 配置触发；`LINUX` 编译符号永远为 false。
- `SignalMap` 的 OPC UA 用户名/密码以明文存进 flowsheet 的 `ExtraProperties`。共享 flowsheet 前需手动清空密码。
- `NModbus` / `OPCFoundation.NetStandard.Opc.Ua.Client` 包等到 T-3.4/T-3.5 再加，避免 T-3.2 引入未使用的依赖冲突。
- T-3.1 的两个 UI 窗体（WinForms / Eto）还没接 `IDynamicsHost`，单步前进/后退也仍在旧循环里。headless runner 已通过水罐样例验证。
- 6 个动态样例的回归基准尚未采集；目前 CI 只跑 `Dynamic Simulation - Water Tank Level Control.dwxmz`。
- `ControlArbiter` 尚未被 runner 或桥调用；T-3.4 的 `ApplyInputs` 负责接入。
- Gate 0 V-3（macOS + Mono）仍未验证。
- T-1.2 的 ALK 半经验系数（`a_act`/`b_act`/`R_ohm_ref`/`E_act_ohm`）、Faraday 低载曲线和 `crossover_coeff` 是占位值，等 R9/R2 数据标定；Bzovsky 2024 的 <2%/<3% 数值对比也待论文数据。
- 电解槽的 `Calculate` 已实现，但还没有带完整 flowsheet 的质量/能量守恒回归；等 T-4.1 的流程模板和 T-5.2 的黄金数据一起补。
- V-5.1.2 ~ V-5.1.10 需要 OpenPLC v4 Docker/真实主站，本会话只完成了 V-5.1.1（Modbus 自环）。
- OPC UA 包从计划里的 1.5.374.54 改为 **1.4.371.96**：前者只有 net48/netstandard2.1，net462 无法引用；1.4.371.96 有 net462 资产。None/Anonymous + mock server 已通过；`Basic256Sha256` + Username、OpenPLC v4、断线重连和权限不足场景未测。
- `Microsoft.Bcl.HashCode` 在 MSBuild 里报 MSB3245（引用未解析），运行时靠 CI 手工复制该 DLL。做发行包时必须把它补进 `DistPackages`。