# H2sim 二期范围：Windows/macOS 双平台 + OpenPLC 外部控制器耦合（OPC UA / Modbus TCP）

配套文档：`docs/h2-electrolyzer-retrofit-analysis.md`（电解槽模型改造部分，仍然有效）
本文件只定义**平台收敛**与**外部控制器桥接**这两块新增范围。

---

## 0. 需求复述与结论

需求：

1. 只保留 **Windows** 和 **macOS** 两个发行平台。
2. 具备与自研开源控制器 **OpenPLC** 通过 **OPC UA** 或 **Modbus TCP** 通讯的能力，让外部 PLC 与 H2sim 的动态模型闭环耦合。
3. **保留** H2sim 自带的内部控制（PID / PythonController / EventSet / CauseAndEffect），用于基础调试和动态模型自测。

结论：

### 已确认的边界条件（本次定稿）

| # | 事项 | 决定 |
|---|---|---|
| 1 | OpenPLC 版本 | **只对 v4**（`autonomy-logic/openplc-runtime`）验证 |
| 2 | Modbus 角色 | **H2sim 当从站**（server），OpenPLC 用 `modbus_master` 主动轮询 |
| 3 | Pro 版 | **保留**。但核查结果：`DWSIM.ProFeatures` 只被 `DWSIM/DWSIM.vbproj`（Classic UI）引用，且引用 `System.Windows.Forms`/`System.Drawing` → **Pro 只在 Windows 经典 UI 存在，macOS 上本来就没有** |
| 4 | macOS 目标机 | **Apple Silicon**（arm64）。这是一项硬约束，改变了 §1.3 与 P0 的结论 |
| 5 | 闭环验证 | **虚拟 I/O**（OpenPLC Docker/本地进程 + 虚拟点位），无真实 PLC 硬件 |

| 判断 | 内容 |
|---|---|
| 可行 | 两项都能做，且**不需要推翻 DWSIM 架构**；但有一处必须动的伤：动态仿真主循环目前**只存在于两个 UI 窗体里**，没有 headless 版本，必须抽出来 |
| 最关键的缺口 | 没有"无 UI 动态运行器"。`FormDynamicsIntegratorControl.vb`（WinForms，396 行循环）与 `DynamicsIntegratorControls.cs`（Eto，同构）各写了一遍，中间夹着 `Application.DoEvents()`、`Flowsheet.RunCodeOnUIThread`、图表刷新、Historian。`Automation3` **完全没有**动态 API；`Flowsheet2` 只赋了 `RunSchedule` 但没有循环体 |
| 第二关键缺口 | 没有信号映射与所有权仲裁。哪个变量由本地 PID 管、哪个由 PLC 管、切换时如何无扰，现在就一个 `PIDController.ManualOverride`（全有或全无），不够用 |
| 通讯侧工作量 | 比想象的**小**：OPC UA 和 Modbus 都有能在 `net462` 上跑的现成库（见 §3），两个桥各 2–3 人周 |
| 平台收敛的收益 | 可以砍掉 GTK/GTK3/WPF/Linux/Raspberry 的编译配置与 `PlatformFiles/Linux`、`PlatformFiles/Raspberry`，构建时间与产物体积明显下降 |
| 平台收敛的代价 | macOS 有两个真问题：**`PlatformFiles/macOS/*.dylib` 全是 x86_64**（本机是 arm64），`ThermoCS/*` 是 **i386 二进制**（Apple Silicon 上 Rosetta 也跑不了 i386）。见 §2.2 |

---

## 1. 现状：平台与构建

### 1.1 当前平台矩阵（`DWSIM.sln` 实际配置）

| 配置 | 用途 | 是否保留 |
|---|---|---|
| `Debug\|x64`、`ReleaseWinMac\|x64` | Windows 主发行（Classic UI WinForms + Eto） | ✅ |
| `ReleaseWinMacP\|*` | Pro 版构建 | ✅（看是否需要 Pro） |
| `ReleaseMac\|*` | macOS（实际映射到 `ReleaseLinux` 编译，靠 Eto.Mac/Mono 运行） | ✅ |
| `ReleaseLinux\|*`、`DebugLinux\|*` | Linux | ❌ 删 |
| `ReleaseWine64\|*` | Wine 交叉 | ❌ 删 |

工程侧可裁：

| 项目 / 资源 | 处置 | 备注 |
|---|---|---|
| `DWSIM.UI.Desktop.GTK` | 删 | **已死代码**：`Program.cs` 里没有任何分支引用它，`sln` 里有 57 处配置但无代码使用 |
| `DWSIM.UI.Desktop.GTK3` | 保留或删 | 只有 macOS 用户把 `MacOSRenderer` 设成 `Gtk3` 时才用；默认走 `Eto.Mac`。建议**保留但降级**为可选，若确认无人用可删（删除需改 `DWSIM.UI.Desktop.csproj` 的 `ProjectReference` 与 `Program.cs` 的两个 `case`） |
| `DWSIM.UI.Desktop.WPF` | 保留 | Windows 用户可以选 WPF 渲染器；删掉会影响 `WindowsRenderer` 分支 |
| `PlatformFiles/Linux`、`PlatformFiles/Raspberry` | 删 | 纯 native 依赖，没有对应平台就用不上 |
| `DWSIM.Apps.AzureServer` | 建议删 | 云求解服务器，与本地闭环无关 |
| `DWSIM.Simulate365` | 建议删 | 第三方云服务对接，同上 |
| `DWSIM.UI.Web` | 保留（可选） | 若将来要做"无 UI 下的远程监视"，这个 WebView 壳有用 |
| `DWSIM.XFlowsheet` | 保留 | Excel 接口，成本分析场景会用 |

### 1.2 UI 与运行时的真实关系（重要，决定桥接放哪）

`DWSIM.UI.Desktop/Program.cs` 的平台选择是**运行时**决定，不是编译期：

```
Windows → WindowsRenderer: WinForms | WPF | Gtk3
Linux   → 强制 GTK3
macOS   → AutomationMode ? GTK3 : MacOSRenderer{ MonoMac | Gtk3 | WinForms }
```

三点推论：

1. **UI 框架必须保留多个 handler**，否则 macOS 用户切换渲染器会崩。所以"只留 Windows+macOS"是**砍 Linux/Raspberry 的构建与二进制**，不是砍 GTK3 代码。
2. Classic UI（`DWSIM.exe`，WinForms）和 Cross-Platform UI（`DWSIM.UI.Desktop.exe`，Eto）是**两套并行实现**，共享 `DWSIM.Interfaces` / `DWSIM.Thermodynamics` / `DWSIM.UnitOperations` / `DWSIM.FlowsheetSolver`。
3. **桥接与动态运行器必须放在 Common 层**（`DWSIM.FlowsheetSolver` 或新项目），不能放 UI 层——否则要为两套 UI 各写一遍，就是现在动态循环的毛病。

### 1.3 macOS / Apple Silicon 的真问题（本次实测结论，决定 P0 规模）

已确认目标机是 Apple Silicon，于是把 macOS 这条链**全部拆开验了一遍**。结论比预想的严重，逐项列出（含 `file` / `lipo` / `otool` 实测结果）：

#### A. native 动态库架构（实测 `lipo -info`）

| 库 | 实测架构 | Apple Silicon 能否原生 |
|---|---|---|
| `libCoolProp.dylib` | x86_64 | ❌ 需 Rosetta |
| `libipopt.dylib` | x86_64 | ❌ 需 Rosetta |
| `liblpsolve55.dylib` | x86_64 | ❌ 需 Rosetta |
| `libscintilla.dylib` | x86_64 | ❌ 需 Rosetta |
| `libcoinmetis.dylib` / `libcoinmumps.dylib` | x86_64 | ❌ 需 Rosetta |
| `libPetAz.dylib` | x86_64 | ❌ 需 Rosetta |
| `libSkiaSharp.dylib`（随包 1.68.2.1） | **i386 + x86_64**（无 arm64） | ❌ 需 Rosetta |

对比：NuGet `SkiaSharp.NativeAssets.macOS 2.88.9` 的 `libSkiaSharp.dylib` 实测是 **x86_64 + arm64 fat**，有 arm64。但升到 2.88 要改 API（`SKFilterQuality` 已废弃，全仓库 94 处 `SKFilterQuality` 用法 + 148 个文件引用 SkiaSharp），属于单独一个大改造，不作为二期内容。

#### B. 两个「比 Rosetta 还糟」的硬伤

| 硬伤 | 实测 | 后果 |
|---|---|---|
| **`PlatformFiles/macOS/ThermoCS/*` 是 Mach-O i386（32 位）** | `file ThermoCS/charact1` → `Mach-O executable i386` | **Rosetta 2 不支持 i386**（只支持 x86_64）。这些二进制在 Apple Silicon 上完全无法执行。且目录里**没有 `platform.txt`**，而 `ThermoCPropertyPackage` 构造函数靠它判 32/64 位（`ThermoCPropertyPackage.cs` L97–112），缺失时 `is64` 保持 `true`，与实际 32 位二进制矛盾 | ThermoC 物性包在 Apple Silicon 上**必须禁用或重编**。12 MB 的死重量 |
| **`libipopt.dylib` 依赖未随包的 gfortran** | `otool -L` 显示依赖 `/usr/local/gfortran/lib/libgfortran.3.dylib` 与 `libquadmath.0.dylib`；且自身 install name 是 `/Users/Daniel/Desktop/Ipopt-3.12.8/lib/libipopt.1.dylib`（上游作者本机绝对路径） | 用户在 macOS 上必须自行装 gfortran 才能用 IPOPT。**当前 macOS 这个库本来就是「有条件可用」状态** |

#### C. macOS UI 的 arm64 死角（最关键）

`Program.cs` L143 默认走 `Eto.Mac.Platform()`，其实现来自：

- `Eto.Platform.XamMac2 2.8.2` 的 `lib/net462/Eto.XamMac2.dll`（**这是 net462 唯一可用的 macOS Eto 后端**）
- 它 P/Invoke 到 `Xamarin.Mac` 原生框架（`DWSIM/References/Xamarin.Mac.dll` 只是托管壳）

而 `Eto.Platform.macOS 2.8.3` 包里的 `Eto.macOS.dll` 目标框架是 **`net6.0-macos10.15`**（实测 nupkg 目录），**net462 工程根本引用不了**——它只被用来引入 `build/*.targets` 做 `.app` 打包。

结论：**macOS 默认的「原生」界面（MonoMac/Xamarin.Mac 后端）没有 arm64 版本**。在 Apple Silicon 上，走这条路只能整个进程跑 Rosetta。

#### D. 由此得到的 macOS 三条路

| 方案 | 内容 | 工作量 | 评价 |
|---|---|---|---|
| **M1：全 Rosetta** | 整个 app 以 x86_64 运行（`Architectures=x86_64`，Rosetta 自动接管） | ~0（只改打包参数） | 能跑，但性能损失 20–40%，且 i386 的 ThermoCS 仍不可用 |
| **M2：GTK3 渲染器 + arm64** | macOS 上把 `MacOSRenderer` 默认改为 `Gtk3`（`Eto.GtkSharp` + Homebrew GTK 有 arm64），native 库仍 x86_64 但只影响那几个子功能 | 3–5 天（改默认值、验证 GTK3 在 macOS 的菜单/快捷键、打包 GTK 依赖） | **推荐**。这是唯一能让主界面 arm64 原生的路径，代价是界面不那么「原生」 |
| **M3：迁 .NET 8 + Eto 2.8 macOS(net6.0)** | 全仓从 net462 迁到 net8.0，用 `Eto.Platform.macOS` 真正的 arm64 后端 | 8–12 人周 | 正解但太重，单独立项 |

**二期决定：走 M1 作为保底（P0 内完成），M2 作为 iOS 之后的第一个优化项（P0 内评估，P6 后执行）。M3 明确排除在二期外。**

#### E. 其它需要记住的 macOS 事实

| 项 | 值 |
|---|---|
| 运行时 | .NET Framework 4.6.2 + Mono（`Settings.IsRunningOnMono()` 靠 `Type.GetType("Mono.Runtime")` 判断） |
| 配置文件位置 | `~/Documents/DWSIM Application Data/` |
| 平台判定 | `ReleaseMac|x64` 在 `DWSIM.sln` 里**映射到 `Release|x64`**（不是 `ReleaseLinux`），所以 macOS 二进制不带 `LINUX` 编译符号 |
| Pro 模块 | macOS 上完全没有（`ProFeatures` 只被 Classic UI 引用且依赖 `System.Windows.Forms`） |

---

## 2. 总体架构

```
┌─────────────────────────────────────────────────────────────┐
│ OpenPLC Runtime（外部控制器）                                 │
│   v4：opcua 插件（asyncua，**仅 server**，默认 4840）          │
│   v4：modbus_slave（pymodbus server，502）                    │
│   v4：modbus_master（pymodbus client，可主动轮询外部设备）      │
│   v3：只有 Modbus（无 OPC UA）                                │
└──────────────┬──────────────────────────────────────────────┘
               │ Modbus TCP :502  /  OPC UA opc.tcp://host:4840
┌──────────────▼──────────────────────────────────────────────┐
│ 【新增】DWSIM.H2sim.Io（C#, net462，独立项目）                 │
│   IPlantBridge（抽象：Read/Write/Quality/Timestamp）           │
│   ├ ModbusTcpServerBridge   （H2sim 当从站，PLC 当主站）        │
│   ├ ModbusTcpClientBridge   （H2sim 当主站，PLC 当从站）        │
│   ├ OpcUaClientBridge       （连 OpenPLC v4 的 OPC UA server） │
│   └ OpcUaServerBridge       （可选：让 SCADA/UAExpert 观察模型）│
│   SignalMap（tag ↔ 对象属性）、单位换算、死区、零阶保持、质量码 │
│   ControlArbiter（本地 PID / 外部 PLC / 手动 三方所有权仲裁）    │
└──────────────┬──────────────────────────────────────────────┘
               │ 无锁双缓冲（写入快照 + 版本号），绝不阻塞模型步
┌──────────────▼──────────────────────────────────────────────┐
│ 【新增】DWSIM.Automation.Dynamics（C#, net462）               │
│   HeadlessDynamicsRunner：固定墙钟步长、超时检测、与 UI 解耦    │
│   （把两处 UI 里的 396 行循环抽成一份，UI 改为调用它）           │
└──────────────┬──────────────────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────────────────┐
│ DWSIM 模型层（现有，不改契约）                                 │
│   电解槽 UO · Vessel/Tank（AccumulationStream）· PID/Python    │
│   Controller · EventSet · CauseAndEffectMatrix                │
└─────────────────────────────────────────────────────────────┘
```

三种运行模式（同一份 flowsheet，切换不重建）：

| 模式 | 谁在控制 | 用途 |
|---|---|---|
| `Local` | 内部 PID / PythonController / EventSet | 现状，基础调试、动态模型自测、控制器整定（`FormPIDTuning` 已有） |
| `External` | 只有 OpenPLC；内部控制器进入 Tracking（跟随但不写） | 外部控制器在环测试（HIL / SIL） |
| `Shadow` | 内部与外部同时算，内部只算不写，记录两者输出偏差 | 控制器对比验证、外部逻辑上线前的旁路校验 |

默认 `Local`，不改变现有行为。

---

## 3. 通讯技术选型（必须满足 .NET Framework 4.6.2）

这一节是硬约束，选错库会卡死。

### 3.1 OPC UA

| 候选 | 目标框架 | 结论 |
|---|---|---|
| **`OPCFoundation.NetStandard.Opc.Ua.Core` 1.5.374.54** | `netstandard2.0`、`netstandard2.1`、`net48`、`net6.0`、`net8.0` | ✅ **选它**（锁定此版本）。`net462` 走 `netstandard2.0` 资产（.NET Framework 4.6.1+ 官方支持 netstandard2.0）。用 `1.4.372.106` 亦可，`1.5.374.54` 更新的同时仍保留 ns2.0 |
| `OPCFoundation.NetStandard.Opc.Ua.Core` 1.5.375.457+ | 最低 `net472` + `netstandard2.0` | 也可，但 `net472` 资产用不上，仍是走 ns2.0 |
| `OPCFoundation.NetStandard.Opc.Ua.Core` **2.0.2** | 最低 `netstandard2.1` | ❌ **不能用**。netstandard2.1 需要 .NET Core 3.0+，`net462` 不兼容 |
| 自研精简栈（如 LibUA） | 视实现 | 仅在需要极致轻量时考虑，不建议 |

依赖副作用：`Opc.Ua.Core` 会带入 `Microsoft.Extensions.Logging`、`System.Memory`、`System.Buffers`、`System.Runtime.CompilerServices.Unsafe` 等一串 ns2.0 包。DWSIM 已经在用 `System.Memory`、`System.Buffers`、`System.Runtime.CompilerServices.Unsafe`、`System.Threading.Tasks.Extensions`（见 `DWSIM.UnitOperations/packages.config`），**版本对齐**时要小心 `System.Runtime.CompilerServices.Unsafe` 的版本冲突（现在 6.1.0，OPC UA 可能要求其他版本）→ 必须在 `app.config` 加 `bindingRedirect`。

**关键协议事实（决定谁是 server）**：

- OpenPLC **v4** 的 OPC UA 插件是 `asyncua` 写的 **纯 server**（`plugin.py` 里只有 `OpcuaServerManager`，没有 client 角色）。所以：
  - 接 v4 → **H2sim 必须当 OPC UA client**。
  - 想额外提供 SCADA 观察 → 再加一个 H2sim 侧 server（可选，P6）。
- NodeId 形态：配置里给 `node_id`（如 `PLC.Example.real_var`），运行时生成 `ns=<idx>;s=<node_id>` 的字符串 NodeId，命名空间 URI 默认 `urn:openplc:opcua:namespace`。所以 **H2sim 侧要能配置"命名空间 URI + 字符串标识符"**，不能只支持 `i=` 数字 NodeId。
- 写权限：v4 按 `viewer/operator/engineer` 角色和每节点 `"r"` / `"rw"` 控制；`operator`/`engineer` 才能写。**H2sim 连接的账号必须至少 `operator`**，否则写入被 `PreWrite` 拒绝（返回 `Access denied: insufficient read permissions` 类错误）。
- 订阅：v4 支持订阅（`SUBSCRIPTION_IMPLEMENTATION.md`），H2sim 应优先用 Subscription（MonitoredItem）拉取，而不是高频 Read——延迟更低、PLC 侧负担更小。
- OpenPLC **v3 完全没有 OPC UA**（v3 仓库里 `opcua` 零命中），只有 Modbus。所以"OPC UA 路线"实际绑定 v4。

### 3.2 Modbus TCP

| 候选 | 目标框架 | 结论 |
|---|---|---|
| **`NModbus` 3.0.81** | 有 **`net46`** 资产 | ✅ **选它**（MIT 许可，`net46` 直接适配 `net462`）。已实测 DLL 内含 `CreateSlaveNetwork` / `IModbusTcpSlaveNetwork` / `ModbusTcpSlaveNetwork`，**从站能力完整** |
| `FluentModbus` 5.3.2 | `netstandard2.0` / `netstandard2.1` | 可用（走 ns2.0），API 更现代，但浮点/多寄存器处理需要自己封装 |
| `NModbus4` | 已弃维 | ❌ |

**关键协议事实（决定谁是 server）**：

OpenPLC v4 同时提供 `modbus_slave`（server）和 `modbus_master`（client）两个插件，所以两条路都通：

- **路线 A（推荐，符合"被控对象"惯例）**：H2sim = Modbus **server**（从站，502），OpenPLC 用 `modbus_master` 主动轮询。理由：物理世界里 PLC 永远是主站去读传感器、写执行器；模型扮演"虚拟现场设备"最自然，也最容易替换成真实设备。
- **路线 B（最省事，复用 OpenPLC 最成熟的路径）**：OpenPLC 用 `modbus_slave`（server），H2sim = Modbus **client** 主动读写。理由：`modbus_slave` 是 OpenPLC 最久经考验的插件，不用动 PLC 侧配置。缺点：H2sim 的步长决定采样率，PLC 侧看不到"模型没跑"这种异常。

**已定：只做 H2sim 当从站（路线 A）**。理由见上；`IPlantBridge` 仍保留抽象，`ModbusTcpClientBridge` 降级为 P6 可选项（NModbus 两边 API 都在，将来加不费事）。

**已核对 OpenPLC v4 `modbus_master` 的实际行为**（`modbus_master_utils.py`）：

- 读：按 FC `1 / 2 / 3 / 4` 分组批量读（`read_requests[fc]`）
- 写：按 FC `5 / 6 / 15 / 16` 分组批量写
- `%MD`（DINT）**每个元素占 2 个寄存器**，`%ML`（LINT）占 4 个——`point.fc in (3,4)` 时按 IEC 元素宽度换算寄存器数
- 布尔量（FC 1/2）返回 `False/True`，寄存器（FC 3/4）返回整数

所以 H2sim 侧作为从站，**必须把同一个区域内的点位按地址连续排布**，否则 OpenPLC 会把它们拆成多个请求，延长扫描周期。

**Modbus 数据模型陷阱（必须在设计阶段解决）**：

OpenPLC v4 的 `modbus_slave` 分段是：

```
holding registers: %QW(int, 0-1023) | %MW(int, 1024-2047) | %MD(dint, 2048-4095, 2 regs/value) | %ML(lint, 4096-8191, 4 regs/value)
coils:             %QX(bits, 8192)
discrete inputs:   %IX(bits, 8192)
input registers:   %IW(int, 0-1024)
```

**没有 REAL/float32 段**。所以：

- 模拟量要么**定点缩放成 INT**（温度 ×10、压力 ×100、功率 ×10），要么塞进 `%MD` 当整型再在两侧转换，要么用 2 个寄存器手工拼 IEEE754。
- **推荐定点缩放**，理由：可读（UAExpert / PLC 监视表里直接看得懂）、无字节序风险、OpenPLC 侧 `INT_TO_REAL` 一行搞定。
- 若一定要传 float32：必须先确认**字序**（大端 vs 小端；Modbus 是 big-endian 寄存器，float32 跨两寄存器的顺序有 ABCD / CDAB / BADC / DCBA 四种约定），写进 `SignalMap` 的 `WordOrder` 字段。
- 线圈量（启停、报警、联锁）走 `%QX` / `%IX`，不要塞进寄存器。

### 3.3 为什么不用 OPC UA 走 Modbus，或反过来

用户同时要两种，不是二选一。正确做法：**`IPlantBridge` 抽象 + 多实现 + 可同时启用**。理由：调试期用 Modbus（简单、wireshark 友好），上线或接 SCADA 用 OPC UA（类型丰富、有订阅、有安全）；两者同时开没有任何冲突。

---

## 4. 核心设计

### 4.1 必须做的重构：抽出 Headless Dynamics Runner

这是整个二期**唯一一处必须改现有代码结构**的地方。

现状（两处同构重复）：

| 位置 | 行数 | 依赖 UI 的东西 |
|---|---|---|
| `DWSIM/Forms/FlowsheetComponents/FormDynamicsIntegratorControl.vb` `RunIntegrator()` L265–660 | ~396 | `Application.DoEvents()`、`Flowsheet.RunCodeOnUIThread`、`ProgressBar1`、`FormDynamics.UpdateControllerList`、`FormSurface.FControl.Invalidate`、`My.Resources.icons8_play`、`Flowsheet.Clone()` + `FormFlowsheet` 强转 |
| `DWSIM.UI.Desktop.Editors/Dynamics/DynamicsIntegratorControls.cs` | ~等价 | 同上（Eto 版） |

抽取方案：

```
新项目 DWSIM.Automation.Dynamics (C#, net462)
  IDynamicsHost      // 抽象 UI 钩子：OnStepBegin / OnStepEnd / OnProgress / ShouldAbort
  HeadlessDynamicsHost : IDynamicsHost   // 空实现，无 UI
  WinFormsDynamicsHost : 现有 WinForms 行为
  EtoDynamicsHost      : 现有 Eto 行为
  DynamicsRunner.Run(IFlowsheet fs, IDynamicsHost host, RunOptions opt)
```

`RunOptions` 至少包含：

```csharp
class RunOptions {
    bool RealTime;                 // 按墙钟走 vs 全速跑
    double StepSeconds;            // 覆盖 integrator.IntegrationStep
    int    ControlDecimation;      // 每 N 步算一次控制（对应 CalculationRateControl）
    int    EquilibriumDecimation;
    int    PressureFlowDecimation;
    double DurationSeconds;        // 0 = 无限
    TimeSpan StartupTimeout;       // 初始稳态求解超时
    double OverrunPolicy;          // Skip | StretchGrid | Abort
    Action<int,double> OnStep;     // (stepIndex, simTime)
}
```

两个 UI 里的 396 行改成"构造 host + 调 `DynamicsRunner.Run`"，代码量各自降到 ~40 行。**这一步同时也是在给外部时间片驱动（§4.3）铺路**。

顺带修掉的现有问题：

- `FormDynamicsIntegratorControl` 里 `Application.DoEvents()` 在后台任务线程上下文里调用（L421/L430），是 WinForms 反模式，容易重入。抽出来顺手去掉。
- `Flowsheet.Clone()` + `DirectCast(..., FormFlowsheet).Dispose()` 这种"克隆整个 flowsheet 来算"的做法在 headless 模式下不能有。

### 4.2 信号映射（SignalMap）

不要在代码里硬编码 tag。用一份随 flowsheet 保存的映射表，UI 里可编辑。

数据模型：

```xml
<SignalMap Version="1">
  <Connection Name="OpenPLC" Protocol="ModbusTcpServer" Host="0.0.0.0" Port="502"
              UnitId="1" Enabled="true"/>
  <!-- 或 Protocol="ModbusTcpClient" Host="192.168.1.10" Port="502" PollMs="50" -->
  <!-- 或 Protocol="OpcUaClient" Endpoint="opc.tcp://192.168.1.10:4840/openplc/opcua"
             NamespaceUri="urn:openplc:opcua:namespace" User="h2sim" Password="***" -->

  <Signal Id="WE1.P_set"
          Direction="FromPlc"
          Object="WE-1" Property="PowerSetpoint"           Unit="kW"
          Type="Int16" Scale="0.1" Offset="0" Deadband="0.5"
          ModbusAddress="40001"                            <!-- 或 Area="HoldingRegister" Offset="0" -->
          OpcNode="PLC.H2.WE1.P_set" WriteMode="OnChange"/>

  <Signal Id="WE1.T_stack"
          Direction="ToPlc"
          Object="WE-1" Property="StackTemperature"        Unit="degC"
          Type="Int16" Scale="10" Offset="0"
          ModbusAddress="30001"
          OpcNode="PLC.H2.WE1.T_stack" UpdateMode="OnStep"/>

  <Signal Id="WE1.Trip" Direction="ToPlc" Type="Bool"
          Object="WE-1" Property="SafetyTrip" ModbusCoil="00001" OpcNode="PLC.H2.WE1.Trip"/>
</SignalMap>
```

`Signal` 字段说明：

| 字段 | 作用 |
|---|---|
| `Direction` | `FromPlc`（PLC 写模型）/ `ToPlc`（模型被 PLC 读） |
| `Type` | `Int16` / `Int32` / `Float32` / `Bool`，决定寄存器占用与转换 |
| `Scale` / `Offset` | 工程值 → 原始值的线性映射；**必须显式**，不搞默认 |
| `Deadband` | 变化才写，减少 PLC 侧无意义写入与网络抖动 |
| `WordOrder` | 仅 `Int32`/`Float32` 有意义：`ABCD`/`CDAB`/`BADC`/`DCBA` |
| `Quality` | 模型算失败/未收敛时如何上报（Modbus 无质量码 → 约定用 `0x8000` 特殊值或独立线圈；OPC UA 用 StatusCode `Bad`） |
| `Clamp` | 越界处理：`Reject`（保留旧值 + 报警）/ `Clamp` |
| `OnWriteAction` | 写失败（越界、对象不存在）记日志并报警，**不抛给模型步** |

**质量码是必须的**：DWSIM 计算抛异常时（比如电解槽 `CellVoltage < Vrev`），桥必须让 PLC 知道"这个值是坏的"，否则 PLC 会拿旧值/0 值当真值做控制，是典型的隐蔽失效。

### 4.3 时序契约（这是闭环能否稳定的决定性因素）

三层时钟：

| 层 | 频率 | 现状 |
|---|---|---|
| OpenPLC 扫描 | 典型 10–50 ms（v4 有 `cycle_time_ms` 配置） | PLC 决定 |
| H2sim 动态步 | 默认 `IntegrationStep` = 5 s，RealTime 模式按 `RealTimeStepMs`（默认 1000） | 太粗，不能直接耦合 |
| 桥 I/O | Modbus 轮询 / OPC UA 发布间隔 | 新增 |

要求：

1. **H2sim 步长必须能被外部指定且与 PLC 扫描成整数倍关系**。建议：
   - 过程级耦合（温度、液位、压力）：`StepSeconds = 0.5 ~ 2 s`，控制抽帧对齐 PLC；
   - 电气级耦合（功率/电流快速跟踪）：`StepSeconds = 50 ~ 200 ms`，但此时电解槽必须开热模型子步（见一期 §4.2 的子步设计），否则热惯性积分发散。
2. **零阶保持（ZOH）**：PLC 在两个模型步之间看到的是上一个步的值，这是物理正确的（真实 DCS 也这样）。**不要插值**——插值会掩盖模型步长过粗的问题，让用户误以为精度够。
3. **超时策略**：模型单步超过 `StepSeconds × 1.5` 时按 `OverrunPolicy`：
   - `Skip`：丢这一步，PLC 看到值不变 + 质量码降级（推荐，最接近真实"控制器还在跑、现场设备卡了"）；
   - `StretchGrid`：真实时间轴拉伸（会让 PLC 与模型时间漂移，仅调试用，要显式警告）；
   - `Abort`：直接停机。
4. **时间戳**：桥必须给每个输出值打 `DateTime` 时间戳，并在 UI/日志里记录"模型时间 vs 墙钟时间"偏差曲线。**这是排查闭环振荡的第一手证据**——振荡 80% 是时间对齐问题，不是模型问题。
5. **RealTime 模式的现有实现是 `Task.Delay(waittime)` 补时间**（`FormDynamicsIntegratorControl.vb` L545–549），没有超时检测、没有抖动统计。抽到 `DynamicsRunner` 时顺手加 `wallClockLag` / `stepDuration` 统计并入 Historian。

### 4.4 控制所有权仲裁（ControlArbiter）

这是"保留内部控制器 + 外部 PLC 也能控"的核心，必须显式设计。

现状：`PIDController` 只有 `ManualOverride`（True 时不写，只读当前 MV）和 `Active`。这不够——ManualOverride 时它不跟踪积分项，切回自动会有积分冲击。

设计：

```vb
' 新增枚举（DWSIM.Interfaces/Enums.vb）
Public Enum ControlOwnership
    Local = 0        ' 内部 PID/Python 写
    External = 1     ' 外部 PLC 写，内部控制器只读不写（Tracking）
    Manual = 2       ' 谁都不写，人工在 UI 里改
    Shadow = 3       ' 内部算但不写，记录与外部值偏差
End Enum
```

按 **每个被控变量（MV）** 而不是每个控制器来定所有权，因为一个 PLC 可能只接管部分回路。

规则：

| 情形 | 行为 |
|---|---|
| `Local` | 与现状完全一致 |
| `External` | 桥写入 MV；对应 PID 设 `Active=False` 但继续执行 `UpdateVars()` 与积分跟踪（新增 `TrackingMode=True`），使 `ITerm` 与实际 MV 一致 |
| `Shadow` | PID 正常算，结果只进历史与偏差统计，不 `SetPropertyValue` |
| `Manual` | 谁都别写，UI 可改 |
| 冲突（两个来源同时写同一 MV） | **仲裁器拒绝第二次写入并报错**，绝不静默覆盖。启动时校验 SignalMap，发现一个 MV 被两个 `FromPlc` 信号指向 → 直接拒绝加载配置 |

PID 需要的增强（一期报告已提，这里补上必要性）：

- `TrackingMode`：自动跟踪外部 MV → 无扰切换（bumpless transfer）
- 输出速率限制（`OutputRateLimit` 单位/s）
- 设定值斜坡（`SetpointRamp`）
- 反算抗饱和（back-calculation），现在只有 `ITerm` 的硬钳位（L752–756），积分饱和在串级时表现差

无扰切换流程：`External → Local` 切换时，先 `ITerm = f(MV_external, PV, SP)` 预置积分项，再置 `Active=True`。**必须做成一步原子操作**，中间不允许有模型步插入。

### 4.5 电解水制氢的标准信号表（建议落成模板）

外部控制器要控的对象清单（也是 SignalMap 模板）：

**PLC → 模型（执行器 / 设定值）**

| Tag | 类型 | 量程 | 说明 |
|---|---|---|---|
| `WE.P_power` | Int16 ×0.1 | 0–5000 kW | 电解槽功率设定（或电流设定，二选一，用 `SetpointMode` 区分） |
| `WE.I_current` | Int16 ×1 | 0–5000 A | 电流设定 |
| `WE.Start` / `WE.Stop` | Bool | — | 启停 |
| `WE.Enable` / `WE.Reset` | Bool | — | 允许/复位 |
| `CWC.V_cool` | Int16 ×0.1 | 0–100 % | 冷却水阀位 |
| `CWP.N_speed` | Int16 ×0.1 | 0–100 % | 碱液循环泵转速（ALK） |
| `SEPH2.V_out` | Int16 ×0.1 | 0–100 % | H2 出口调节阀（背压） |
| `SEPO2.V_out` | Int16 ×0.1 | 0–100 % | O2 侧阀 |
| `PURGE.Enable` | Bool | — | 纯化/吹扫 |

**模型 → PLC（传感器）**

| Tag | 类型 | 量程 | 说明 |
|---|---|---|---|
| `WE.T_stack` | Int16 ×100 | 0–200 °C | 堆温 |
| `WE.U_cell` | Int16 ×1000 | 0–3 V | 单元电压 |
| `WE.I_stack` | Int16 ×1 | 0–5000 A | 堆电流 |
| `WE.i_dens` | Int16 ×100 | 0–5 A/cm² | 电流密度 |
| `WE.P_cathode` | Int16 ×100 | 0–50 bar | 阴极压力 |
| `WE.P_anode` | Int16 ×100 | 0–50 bar | 阳极压力 |
| `H2.m_flow` | Int16 ×10 | 0–1000 kg/h | 产氢质量流量 |
| `H2.purity` | Int16 ×100 | 0–100 % | 氢气纯度 |
| `H2.in_O2` | Int16 ×100 | 0–10 % | **氧中氢（安全关键）** |
| `O2.in_H2` | Int16 ×100 | 0–10 % | 氢中氧 |
| `SEPH2.L_level` | Int16 ×10 | 0–100 % | H2 分离罐液位 |
| `SEPO2.L_level` | Int16 ×10 | 0–100 % | O2 分离罐液位 |
| `CW.T_out` | Int16 ×10 | 0–200 °C | 冷却水出口温度 |
| `WE.SEC` | Int16 ×10 | 0–200 kWh/kg | 比能耗 |
| `ALM.H2inO2_H` / `ALM.H2inO2_HH` | Bool | — | 2% / 4%(LEL) 报警与跳车 |
| `ALM.OverTemp` / `ALM.OverPressure` | Bool | — | 超温/超压 |
| `ST.WE_Running` / `ST.WE_Fault` | Bool | — | 状态 |

分配建议（Modbus 路线 A，H2sim 当从站）：

```
%IW 30001–30024  : 传感器（本表 17 个模拟量 + 4 个布尔用线圈 %IX）
%QX 00001–00020  : 执行器（Bool 部分）
%QW 40001–40030  : 设定值 / 模拟量执行器（Int16）
```
定死一张地址表，写进 `docs/io-map.md`，PLC 程序与 H2sim 模板共用同一张表——**这是两边能长期兼容的关键**。

### 4.6 联锁与安全（外部控制时必须加强）

`External` 模式下内部 PID 不写，但**安全联锁必须独立于所有权**，永远生效：

- 联锁放在模型侧（`ElectrolyzerUnitOpBase` 里），不在 PID 里，也不在桥里。理由：模型侧联锁对 `Local`/`External`/`Shadow` 一律有效，是被控对象的固有属性。
- 触发动作：切断功率输入（把 `PowerSetpoint` 强制为 0 并向 PLC 上报 `WE.Trip`）+ 打开放空/氮气吹扫（若有该对象）。
- 必须有 `Latch` + `Reset` 逻辑，防止 PLC 反复重启到危险工况。
- **`H2.in_O2 > 2%` 高报、`> 4%` 跳车**（4% 是氢氧混合爆炸下限，文献 R2 的核心结论）。
- 桥侧只负责"把报警位传给 PLC"，**不负责判断**。

---

## 5. 实施计划（已按 5 项决定定稿）

### 5.1 阶段清单

| 阶段 | 内容 | 人周 | 依赖 |
|---|---|---|---|
| **P0a** | 构建环境打通：Windows（VS2022/MSBuild + nuget restore）；macOS 侧确认 Mono 版本、`packages/` 还原（**当前 `packages/` 目录不存在**，必须 `msbuild -t:restore`）、跑通现有 samples | 1.5 | — |
| **P0b** | 平台收敛：删 `ReleaseLinux\|*` / `DebugLinux\|*` / `ReleaseWine64\|*` 配置与 `PlatformFiles/Linux`、`PlatformFiles/Raspberry`；删 `DWSIM.UI.Desktop.GTK`（死代码）；`DWSIM.Apps.AzureServer`、`DWSIM.Simulate365` 停编（保留源） | 1 | P0a |
| **P0c** | **Apple Silicon 适配（因决定 4 新增）**：macOS 打包改 `x86_64 + Rosetta`（方案 M1）；禁用/移除 `ThermoCS`（i386，Apple Silicon 不可执行，且缺 `platform.txt`）；把 `libipopt` 的 gfortran 依赖写进安装说明；`DWSIM.ProFeatures` 确认不进 macOS 包 | 2 | P0b |
| P1 | 电解槽电化学模型 + KPI + 单位扩展 | 3–4 | P0a |
| P1b | 电解槽动态支持（热惯性 + 冷却 + 子步） | 1 | P1 |
| P2 | KOH/NaOH 物性 + 电导率关联 + Henry 溶解 | 2 | P0a |
| **P3a** | **抽出 `HeadlessDynamicsRunner`，两个 UI 改为调用它** | **2** | P0a |
| **P3b** | **`SignalMap` + `ControlArbiter` + PID 增强（Tracking/RateLimit/Ramp/back-calc）** | **2** | P3a |
| **P3c** | **Modbus TCP **从站**桥（`NModbus` `CreateSlaveNetwork`）+ `IPlantBridge` 抽象** | **1.5** | P3b |
| **P3d** | **OPC UA Client 桥（连 OpenPLC v4 的 server：订阅 + 写 + 质量码 + 用户名密码/Basic256Sha256）** | **2.5** | P3b |
| P4 | BoP 对象（H2 纯化/储氢）+ 两个流程模板 | 3–4 | P1 |
| **P5** | **虚拟 I/O 闭环验证**：Modbus loopback（自写测试从站/主站脚本）→ OpenPLC v4 Docker 接 Modbus → 同样跑 OPC UA；三回路（温度/压力/液位）阶跃与扰动测试 | **2** | P3c/P3d |
| P6 | 打包发布（Windows 单文件 exe、macOS dmg）+ `VERSION` + `scripts/clean.sh` + `docs/io-map.md` + 项目 `AGENTS.md`/`PROJECTS.md` 登记 | 1.5 | P5 |
| **合计** | | **23–27** | |

相对上一版的变动：P0 从 1.5 → **4.5 人周**（Apple Silicon 适配 + `packages/` 缺失）；P3c 从 2 → **1.5**（只做从站）；P5 保持 2（虚拟 I/O，无硬件调试成本）。

### 5.2 MVP（能跑通"OpenPLC 控电解槽模型"的最小闭环，约 5–6 周）

`P0a` + `P0b` + **`P3a`** + **`P3b`（只做 `Local`/`External`）** + **`P3c`（只做从站）** + 一个电解槽模板（用现有 `WaterElectrolyzer` 接口先顶着，P1 完成后再换）。

`P0c`（Apple Silicon）可以**并行或稍后**：MVP 验证在 Windows 上做完全足够，macOS 打包放到 P6 前完成即可。

MVP 的价值：把"外部控制器 ↔ 动态模型"的**整条管道**先打通并验证时序，再往里填电解槽精度。反过来做（先精修模型再搭管道）风险更大，因为时序问题会全部砸在最后。

---

## 6. 风险与对策（已按 5 项决定更新）

| 风险 | 等级 | 对策 |
|---|---|---|
| **Apple Silicon 上 macOS 主界面无法原生**（`Eto.Platform.XamMac2` 的 net462 后端无 arm64；`Eto.Platform.macOS` 的 `Eto.macOS.dll` 是 `net6.0-macos`，net462 引用不了） | **高** | 方案 M1（全进程 Rosetta）保底，P0c 内完成；M2（GTK3 渲染器 + arm64 主界面）作后续优化；M3（迁 .NET 8）单独立项 |
| **`ThermoCS` 是 i386，Apple Silicon 不可执行** | **高** | P0c 直接禁用：`ThermoCPropertyPackage` 在 macOS/arm64 下拒绝实例化并给明确提示；`ThermoCS/` 目录（12 MB）不进 macOS 包 |
| **`libipopt.dylib` 依赖未随包 gfortran，且 install name 是上游作者本机绝对路径** | **高** | 短期：安装说明写清 `brew install gcc`；中期：`install_name_tool` 重定向或重编 |
| **`packages/` 目录当前不存在**，所有 HintPath 全部悬空 | **高** | P0a 第一步先 `msbuild -t:restore` 还原；把还原命令写进 `scripts/build.sh` |
| 动态循环从 UI 抽出时**行为变化**（现有用户的自定义脚本、事件时序） | 高 | 抽出后两 UI 走同一 runner，既有一期回归测试必须全绿；`RunOptions` 默认值严格等于现有硬编码值（5 s / 1 / 1 / 1） |
| 模型步与 PLC 周期不同步导致闭环振荡，被误判为"模型不准" | 高 | 强制记录"模型时间 vs 墙钟"偏差；MVP 就把偏差曲线做出来 |
| `System.Runtime.CompilerServices.Unsafe` 等 ns2.0 包**版本冲突**（OPC UA 要求 vs DWSIM 现有 6.1.0） | 高 | 先在空项目验证 `net462 + Opc.Ua.Core 1.5.374.54`；必要时加 `bindingRedirect` + `AssemblyResolve` 兜底 |
| OpenPLC v4 OPC UA 写权限需 `operator` 以上 | 中 | 文档写明账号角色要求；桥启动时做一次写入探测，失败就明确报错而不是静默降级 |
| Modbus 无 REAL 段，只能定点缩放 | 中 | 强制 `Scale`/`Offset` 显式；`docs/io-map.md` 定死地址表；`Float32` + `WordOrder` 预留但默认关 |
| OpenPLC `modbus_master` 按 FC 分组批量读写，**点位不连续会拆成多次请求** | 中 | `docs/io-map.md` 要求同一区域内点位地址连续排布；MVP 阶段用 Wireshark 核对请求数 |
| 虚拟 I/O 验证覆盖不到真实网络的抖动/丢包 | 中 | P5 用 `tc`/`clumsy` 注入延迟与丢包做一轮；文档明确"未经真实硬件验证" |
| `net462 + Mono` 的 macOS 长期可维护性 | 中 | 二期不改架构；".NET 8 迁移"列为独立后续项目（8–12 人周） |
| 桥在 UI 进程内跑，OPC UA server 崩溃会带走模型 | 中 | 桥入口 try/catch 到边界、异常不抛给模型步；提供 `H2sim.Headless` 无 UI 宿主（复用 `Flowsheet2`）做长跑 |
| 一个 MV 被两处写，静默覆盖 | 高 | `ControlArbiter` 加载期校验 + 运行期拒绝第二次写入 + 报错 |
| GPLv3 + 商业件混用 | 低 | OPC UA（MIT）、NModbus（MIT）与 GPLv3 兼容；Pro 继续走原有闭源桥，不进本二期 |

---

## 7. 明确不做 / 先不做

1. **不做** OPC UA Server + GDS + 复杂安全（证书链、用户名密码已够）。**原因还多了一条：OpenPLC v4 的 OPC UA 插件是纯 server，它不会连我们的 server**，所以 H2sim 侧只做 client。
2. **不做** OPC UA 复杂类型（`Opc.Ua.Client.ComplexTypes`）与自定义信息模型。扁平变量够用。
3. **不做** Modbus RTU / 串口，也**不做 Modbus 主站角色**（已定为 H2sim 从站）。
4. **不做** 硬实时保证。H2sim 是软实时仿真器，文档必须说清"不适用于安全仪表功能（SIF）验证"。
5. **不做** .NET 8 迁移（单独立项）。
6. **不做** Linux / Raspberry 的继续维护。
7. **不删** GTK3 代码（macOS `MacOSRenderer=Gtk3` 是可行的 arm64 路径，方案 M2 要用），只删 Linux/Raspberry 的构建产物与配置。
8. **不做** 真实 PLC 硬件在环（本阶段只有虚拟 I/O，端口与协议层已完全等价）。

---

## 8. 后续可选优化（不进二期）

| 优化 | 收益 | 代价 |
|---|---|---|
| 方案 M2：macOS 默认改用 `MacOSRenderer=Gtk3` | 主界面 arm64 原生，性能 +20–40% | 3–5 天；界面不那么原生 |
| `libCoolProp` / `libipopt` / `libPetAz` 重编 arm64 | 消灭 Rosetta | 1–2 天/个 + CI 交叉编译 |
| `SkiaSharp` 1.68 → 2.88 | 拿到 arm64 `libSkiaSharp.dylib` | 148 个文件 + 94 处 `SKFilterQuality` 需改 API |
| 迁移 .NET 8 | 摆脱 Mono，用真正的 `Eto.Platform.macOS` arm64 | 8–12 人周 |
| `ModbusTcpClientBridge`（主站角色） | 复用 OpenPLC 最成熟的 `modbus_slave` | 0.5 人周（NModbus API 已在） |
| OPC UA Server 桥（供 SCADA 观察） | 第三方 SCADA 可直接浏览模型 | 2 人周 |

---

## 9. 附：本次实测命令与结果（可复现）

```sh
# macOS native 库架构
lipo -info PlatformFiles/macOS/libSkiaSharp.dylib
# → i386 x86_64                     ← 无 arm64

file PlatformFiles/macOS/ThermoCS/charact1
# → Mach-O executable i386          ← Rosetta 2 不支持 i386

# libipopt 的硬依赖
lipo -info PlatformFiles/macOS/libipopt.dylib          # → x86_64
otool -L  PlatformFiles/macOS/libipopt.dylib
# → /usr/local/gfortran/lib/libgfortran.3.dylib        ← 未随包
# → /Users/Daniel/Desktop/Ipopt-3.12.8/lib/libipopt.1.dylib  ← 上游作者本机路径

# Eto macOS 后端的目标框架
unzip -l Eto.Platform.macOS.2.8.3.nupkg | grep lib/
# → lib/net6.0-macos10.15/Eto.macOS.dll  ← net462 引用不了
unzip -l Eto.Platform.XamMac2.2.8.2.nupkg | grep lib/
# → lib/net462/Eto.XamMac2.dll          ← net462 唯一可用的 macOS 后端

# SkiaSharp 2.88 才有 arm64 native
lipo -info runtimes/osx/native/libSkiaSharp.dylib   # (2.88.9)
# → x86_64 arm64

# OPC UA 库框架支持
unzip -l OPCFoundation.NetStandard.Opc.Ua.Core.1.5.374.54.nupkg | grep lib/
# → net48 net6.0 net8.0 netstandard2.0 netstandard2.1   ← 走 ns2.0

# NModbus 从站能力
unzip -l NModbus.3.0.81.nupkg | grep lib/
# → net46 net6.0 netstandard1.3 netstandard2.0
strings lib/net46/NModbus.dll | grep -i CreateSlave
# → CreateSlaveNetwork / IModbusTcpSlaveNetwork  ✅
```

---

## 10. 已确认事项（原「待确认」已关闭）

| # | 事项 | 已确认 | 对本方案的影响 |
|---|---|---|---|
| 1 | OpenPLC 版本 | **只对 v4** | 可放心用 OPC UA（v3 无）；Modbus 地址段按 v4 分段规则定（`%QW/%MW/%MD/%ML/%QX/%IX/%IW`） |
| 2 | Modbus 角色 | **H2sim 从站** | `P3c` 只做 `ModbusTcpServerBridge`；主站降为 P6 可选项；工作量 2 → 1.5 人周 |
| 3 | Pro 版 | **保留** | 核查结果：`ProFeatures` 只被 Classic UI（`DWSIM/DWSIM.vbproj`）引用且依赖 `System.Windows.Forms` → **Pro 仅存于 Windows，macOS 包本来就无 Pro**，不用额外处理 |
| 4 | macOS | **Apple Silicon** | 新增 `P0c`（2 人周）；确定走方案 **M1（全进程 Rosetta）**；`ThermoCS`（i386）必须禁用；`libipopt` 的 gfortran 依赖要写进安装说明 |
| 5 | 闭环验证 | **虚拟 I/O** | `P5` 保持 2 人周；验证路径：自写 loopback → OpenPLC v4 Docker → 三回路阶跃/扰动；需加一轮 `tc`/`clumsy` 延迟丢包注入；文档需明确「未经真实硬件验证」 |

### 因为决定 4 而新增的一项必做工作（P0c 明细）

| 子项 | 内容 | 人时 |
|---|---|---|
| C1 | macOS 构建配置改 `Architectures=x86_64`，打包脚本写明 Rosetta 要求 | 2 |
| C2 | `ThermoCPropertyPackage` 在 macOS 下主动抛「Apple Silicon 不支持 ThermoC」的明确异常，并把它从物性包列表隐藏（`DWSIM.GlobalSettings.Settings.IsRunningOnMono()` 不够，需判 CPU 架构） | 4 |
| C3 | `ThermoCS/`（12 MB，i386）不进 macOS 包 | 1 |
| C4 | 安装说明补 `brew install gcc`（libipopt 需 `libgfortran.3` / `libquadmath.0`） | 1 |
| C5 | 实测验证：macOS 上跑通一份稳态 flowsheet + 一份动态 flowsheet（排除 ThermoC 物性包） | 8 |

### 尚未关闭的一个技术不确定点（P0a 内必须先验）

**`net462` 工程能否在 Apple Silicon 上用 Rosetta 跑 Mono**。理论上可以（Mono x86_64 + Rosetta 2），但需要实测。若不行，则 macOS 只能走方案 M2（GTK3 + arm64）或直接推迟到 .NET 8（M3）。**建议 P0a 第一件事就是在新机子上跑一遍现有的 `DWSIM.UI.Desktop.exe`，以此作为后续所有排期的前提。**

