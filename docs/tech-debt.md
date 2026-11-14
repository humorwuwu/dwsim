# 技术债

- `DWSIM.Simulate365` 仍参与构建：`DWSIM.SharedClassesCSharp` 和多个 WinForms 窗体直接引用它，不能从 sln 移除。`DWSIM.Apps.AzureServer` 不在 sln 里，无需处理。
- `DWSIM.Plugins.NaturalGas`、`DWSIM.Apps.TCPServer` 等工程的 `PostBuildEvent` 仍含 `DistPackages\Linux` 清理行（T-0.2 只清理了 `DWSIM` 与 `DWSIM.UI.Desktop`）。不影响 Windows 构建，下次动打包时一并删。
- 工程文件里的 `ReleaseLinux|*` / `wine*` PropertyGroup 保留，但已没有 solution 配置触发；`LINUX` 编译符号永远为 false。