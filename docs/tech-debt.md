# 技术债

- `DWSIM.Simulate365` 仍参与构建：`DWSIM.SharedClassesCSharp` 和多个 WinForms 窗体直接引用它，不能从 sln 移除。`DWSIM.Apps.AzureServer` 不在 sln 里，无需处理。
- `DWSIM.Plugins.NaturalGas`、`DWSIM.Apps.TCPServer` 等工程的 `PostBuildEvent` 仍含 `DistPackages\Linux` 清理行（T-0.2 只清理了 `DWSIM` 与 `DWSIM.UI.Desktop`）。不影响 Windows 构建，下次动打包时一并删。
- 工程文件里的 `ReleaseLinux|*` / `wine*` PropertyGroup 保留，但已没有 solution 配置触发；`LINUX` 编译符号永远为 false。
- `SignalMap` 的 OPC UA 用户名/密码以明文存进 flowsheet 的 `ExtraProperties`。共享 flowsheet 前需手动清空密码。
- `NModbus` / `OPCFoundation.NetStandard.Opc.Ua.Client` 包等到 T-3.4/T-3.5 再加，避免 T-3.2 引入未使用的依赖冲突。
- 6 个动态样例中 5 个已进入 headless 冒烟；PFR 样例因步长/停留时间不匹配跳过。数值回归基准尚未采集。
- Gate 0 V-3（macOS + Mono）仍未验证。
- T-1.2 的 ALK 半经验系数（`a_act`/`b_act`/`R_ohm_ref`/`E_act_ohm`）和 `crossover_coeff` 仍是待标定值，等 R9/R2 数据校准。PEM 已加入 Bzovsky 2024 Fig. 2 的 18 点 <2% 极化回归、Fig. 6 的 15 点 <3% SEC 回归、4 点热积分解析回归和低载 LEL 趋势测试；与 Dinh 2021 曲线、R2 数值曲线仍待补齐。
- 外部 UO 图形保存的 `<Description>` 为空、`Tag` 不带 UO 前缀，加载时注册表匹配不到，改由保存的输入/输出数量重建通用连接点，UO 的 `LoadData` 再复用；生成器以后应把 `GraphicObject.Description` 写成 UO 描述。
- 电解槽的 `Calculate` 已有 PEM/ALK 模板和 CI 加载/计算冒烟；模板含 H2 纯化/储罐，但 G/L 分离和两级压缩尚未接入。PEM 额定点按论文 925 kW 上限取 900 kW，ALK 厂级模板使用 17600 cm2 单电池面积；完整质量/能量守恒回归仍待补齐。
- V-5.1.2 ~ V-5.1.10 需要完整 OpenPLC v4 程序与真实主站；已增加 OpenPLC v4 官方 `modbus_master` 的 FC4/FC16 探针（V-5.1.1b）和官方 OPC UA server manager 的读写探针（V-3.5b），但仍未完成 PLC 内三回路 PID。
- OPC UA 包从计划里的 1.5.374.54 改为 **1.4.371.96**：前者只有 net48/netstandard2.1，net462 无法引用；1.4.371.96 有 net462 资产。None/Anonymous 已通过 mock server 和 OpenPLC v4 官方 server manager 探针；`Basic256Sha256` + Username、断线重连和权限不足场景未测。
- `Microsoft.Bcl.HashCode` 在 MSBuild 里报 MSB3245（引用未解析），运行时靠 CI 手工复制该 DLL。做发行包时必须把它补进 `DistPackages`。
- 本地 Mono 构建：`Directory.Build.targets` 为非 Windows 构建提供 netstandard facade 引用和 HintPath 反斜杠归一化；`DWSIM.Controls.PythonConsoleControl` 是 WPF 工程，Mono 无法编译，所以全量本地构建只能做到 Io/Modbus 测试，Dynamics/Automation 仍依赖 CI 产物。
- T-3.1 的两个 UI 窗体已接线并通过编译；暂停/续跑、单步前进/后退和 6 个动态样例的 UI 行为还没有 Windows 桌面实测，回归基准也未采集。
- `Dynamic Simulation - PFR.dwxmz` 保存的 1 s 动态步长大于该样例的 PFR 动态停留时间，直接运行会报 `PFR-001`；headless 冒烟跳过该样例。需要用户手工降低步长或修样例。
