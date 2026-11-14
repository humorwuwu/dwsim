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