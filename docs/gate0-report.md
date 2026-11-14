# Gate 0 前置验证报告

- 日期：2026-11-14
- 仓库：`/Users/wcx/ongoing/H2sim`，分支 `windows`，HEAD `1abf72d1b`
- 执行环境：macOS 27.3（arm64），Rosetta 2 已安装（`oahd` 运行中）
- 工具链现状：`msbuild` / `xbuild` / `mono` / `nuget` / `dotnet` **均未安装**；无 Windows VM、无 SSH 构建主机

## V-1 能否还原 NuGet 依赖

- 事实：`packages/` 目录不存在（`ls packages` 失败），全仓 `HintPath` 悬空。
- 结果：**未执行（BLOCKED）**。本机没有 `nuget` 或 `msbuild`，无法运行 restore。
- 结论：需要可用的还原环境（Windows + msbuild/nuget，或 Mono + NuGet）后才能判定。

## V-2 能否编译并运行现有版本（Windows）

- 结果：**未执行（BLOCKED）**。本机是 macOS，没有 Windows msbuild，也没有 Mono 兜底。
- 结论：同上，依赖 V-1 的还原环境。

## V-3 macOS 上 net462 + Mono 能否运行

- 结果：**未执行（BLOCKED）**。V-3 需要 V-2 产出的 `DWSIM.exe`/`DWSIM.UI.Desktop.exe`，且本机没有 Mono。
- 已确认：Rosetta 2 已安装，`/Library/Apple/usr/libexec/oah` 存在，后续若拿到 x86_64 产物可在本机验证。
- 结论：macOS 路线（M1 全进程 Rosetta / M2 GTK3）尚未选定，取决于 V-3 实测。

## V-4 ThermoCS 是否真的不可用

- `file PlatformFiles/macOS/ThermoCS/charact1` → `Mach-O executable i386`
- `lipo -info` → `Non-fat file ... architecture: i386`
- `PlatformFiles/macOS/ThermoCS/platform.txt` → **不存在**
- `grep -rl "ThermoC" PlatformFiles/Common/samples/ PlatformFiles/Common/tests/` → **无匹配**
- 结果：**PASS**。ThermoCS 确认是 i386，Apple Silicon 无法执行；且无官方样例依赖 ThermoC，T-0.3.2 直接禁用无回归风险。

## 结论

Gate 0 **未通过**，卡点是构建环境，不是代码。V-4 通过；V-1/V-2/V-3 需要先确定构建路线：

1. GitHub Actions `windows-latest`（推荐，与上游 .NET Framework 4.6.2 目标一致，可产出 Windows 包与 macOS 验证用的 IL 产物）
2. 本机安装 Mono + NuGet（arm64 Mono；能否编译 68 个 net462 工程待验证）
3. 外部 Windows 机器/VM 构建，本机只做 macOS 运行验证
4. 先做不依赖构建的 T-0.1 基建，构建类验收后补

在路线确定并跑通 V-1/V-2 之前，不开始 T-0.1 之后的任何任务。