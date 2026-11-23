# 技术债

- `DWSIM.Simulate365` 仍参与构建：`DWSIM.SharedClassesCSharp` 和多个 WinForms 窗体直接引用它，不能从 sln 移除。`DWSIM.Apps.AzureServer` 不在 sln 里，无需处理。
- `DWSIM.Plugins.NaturalGas`、`DWSIM.Apps.TCPServer` 等工程的 `PostBuildEvent` 仍含 `DistPackages\Linux` 清理行（T-0.2 只清理了 `DWSIM` 与 `DWSIM.UI.Desktop`）。不影响 Windows 构建，下次动打包时一并删。
- 工程文件里的 `ReleaseLinux|*` / `wine*` PropertyGroup 保留，但已没有 solution 配置触发；`LINUX` 编译符号永远为 false。
- `SignalMap` 的 OPC UA 用户名/密码以明文存进 flowsheet 的 `ExtraProperties`。共享 flowsheet 前需手动清空密码。
- `NModbus` / `OPCFoundation.NetStandard.Opc.Ua.Client` 包等到 T-3.4/T-3.5 再加，避免 T-3.2 引入未使用的依赖冲突。
- 6 个动态样例都已进入 headless 冒烟；PFR 样例原先 `CatalystVoidFraction=0` 导致停留时间为 0，已在 2026-11-22 修正样例数据（空隙率 0.4、催化剂装填 1000 kg/m3）。数值回归基准尚未采集。
- Gate 0 V-3（macOS + Mono）仍未验证。
- T-1.2 的 ALK 半经验极化系数已按工业 30 wt% KOH / 80 degC 电压包络标定（0.4 A/cm2 约 1.76 V，84% HHV），并加入 6 点回归；`crossover_coeff` 仍是单一系数，`FaradayEfficiency` 已改为随载变化（`i/(i+i_cross)`，参考点由 `i_ref` 指定）。KOH 电导率用 R9/Gambou 2022 Equation 41 并有 6 点回归。仍待：Dinh 2021 原始热曲线与 R2 气体穿透数值曲线。
- 外部 UO 图形保存的 `<Description>` 为空、`Tag` 不带 UO 前缀，加载时注册表匹配不到，改由保存的输入/输出数量重建通用连接点，UO 的 `LoadData` 再复用；生成器以后应把 `GraphicObject.Description` 写成 UO 描述。
- 气体穿透已改为随阴极压力与温度缩放（分压差 + Arrhenius，参考点因子严格为 1），并按两侧互换写入出口流股；`O2 in H2` / `H2 in O2` / `H2Purity` 现在从流股读回，与组分完全一致。`E_perm=20 kJ/mol` 是文献常见量级，`crossover_coeff` 的绝对值仍待标定。
- 电解槽的 `Calculate` 已有 PEM/ALK 模板和 CI 加载/计算冒烟；模板含 H2 纯化、阴极气先冷到 40 °C 再经 `Vessel` 闪蒸分出水、两级压缩（ALK 15.5→90→250 bar，PEM 29.5→90→250 bar，75% 等熵，中间冷却 + 分离罐）与储罐。已保存模板的压缩机 `DeltaQ` 与能量流一致（ALK 71.69 / 37.24 kW，PEM 10.47 / 9.45 kW），计算模式保持出口压力。动态运行已验证（ALK 314.15 K / PEM 310.74 K，冷却水按模型 `CoolingDuty` 定容，PEM 5.80 kg/s、ALK 8.13 kg/s）。完整质量/能量守恒回归已加入（元素级 + 热中性基准）。
- OpenPLC v4 已完成官方 `modbus_master` FC4/FC16 探针（V-5.1.1b）、OPC UA server manager 读写探针（V-3.5b）、Runtime 容器内温度/压力/液位三回路 PI 虚拟台架（V-5.1.2–4），以及 Modbus/OPC UA 结果对照（V-5.1.5）。该台架使用按 H2sim 点表实现的独立过程模型，尚未接入 DWSIM 动态模型；延迟/丢包、Shadow、联锁、时序和超时策略仍待完成。
- OPC UA 包从计划里的 1.5.374.54 改为 **1.4.371.96**：前者只有 net48/netstandard2.1，net462 无法引用；1.4.371.96 有 net462 资产。None/Anonymous 已通过 mock server 和 OpenPLC v4 官方 server manager 探针；`Basic256Sha256` + Username、断线重连和权限不足场景未测。
- `Microsoft.Bcl.HashCode` 在 Mono/MSBuild 里可能报 MSB3245（引用未解析）；Windows CI 现在会在打包前显式复制 HashCode、System.Buffers 和 System.Memory 到 `DistPackages`。Io 测试宿主也带有这些程序集的 binding redirect。
- 本地 Mono 构建：`Directory.Build.targets` 为非 Windows 构建提供 netstandard facade、HintPath 反斜杠归一化，并跳过 WPF 渲染器、两个 WPF Python 控制台和 `licenses.licx`。`DWSIM.Automation.Dynamics.Tests` 可在本机编过并跑通。桌面进程（Gate 0 V-3）仍未验证；macOS 构建里没有 WPF 渲染器、交互式 IronPython 控制台和定位按钮。
- T-3.1 的两个 UI 窗体已接线并通过编译；暂停/续跑、单步前进/后退和 6 个动态样例的 UI 行为还没有 Windows 桌面实测，回归基准也未采集。
- `Dynamic Simulation - PFR.dwxmz` 上游原样保存的 `CatalystVoidFraction=0`、`CatalystLoading=0` 会让停留时间为 0，动态运行必然报 `PFR-001`；该样例数据已在 T-3.1 修正。`RunOptions.StepSeconds` 现在也会传给积分器，调用方可以按需指定更小的步长。
