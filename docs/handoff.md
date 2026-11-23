# 交接文档（2026-11-23）

给接手的 agent：先读 `AGENTS.md`，再读本文件。本文件只写"现在在哪、下一步做什么、别踩什么坑"。

- 仓库：`/Users/wcx/ongoing/H2sim`
- 分支：`h2sim/phase-a`（已推送到 `origin/h2sim/phase-a`）
- 最近提交：`5173f354b`（储罐真实压缩因子）
- 版本：`VERSION` = `0.2.1`，**本轮改动尚未升版**
- 计划与进度：`docs/h2sim-implementation-plan.md`；执行 ledger 在 `.superpowers/sdd/h2sim-implementation-plan/progress.md`（该目录被 git 忽略，只在本机）

## 1. 当前方向（用户明确指示，必须遵守）

1. **优先做 DWSIM 自身的功能和模型**。
2. **PLC / OpenPLC 相关全部暂停**（V-5.1.8–V-5.1.10）。V-5.1.8 的代码已存在但用环境变量 `H2SIM_INTERLOCK_TEST` 关闭，**不要去修、不要打开**。
3. 用户最近的指令：**先让测试在 macOS 本地跑通**，而不是只靠 CI。

## 2. 构建与测试矩阵（实测结论）

| 目标 | 本机 macOS | Windows CI |
|---|---|---|
| `DWSIM.H2sim.Io.Tests` | ✅ 能编能跑（见下） | ✅ |
| `DWSIM.Automation.Dynamics.Tests` | ❌ **编不过**（WPF 阻断） | ✅ |
| `DWSIM` 桌面应用 | 未验证（Gate 0 V-3） | ✅ 能编能启 |

### macOS 本地跑 Io 测试（已验证 PASS）

```bash
cd /Users/wcx/ongoing/H2sim
FACADES=/Library/Frameworks/Mono.framework/Versions/6.12.0/lib/mono/4.7.2-api/Facades

msbuild DWSIM.Interfaces/DWSIM.Interfaces.vbproj /p:Configuration=Release /p:Platform=AnyCPU \
  /p:PostBuildEvent= /p:GenerateSerializationAssemblies=Off /p:NetStandardFacadesPath=$FACADES

msbuild DWSIM.H2sim.Io.Tests/DWSIM.H2sim.Io.Tests.csproj /p:Configuration=Release /p:Platform=AnyCPU \
  /p:PostBuildEvent= /p:GenerateSerializationAssemblies=Off /p:NetStandardFacadesPath=$FACADES

cp DWSIM.Interfaces/bin/Release/DWSIM.Interfaces.dll DWSIM.H2sim.Io.Tests/bin/Release/
cd DWSIM.H2sim.Io.Tests/bin/Release && mono DWSIM.H2sim.Io.Tests.exe   # 期望输出 PASS
```

注意：`DWSIM.Interfaces` 的 ProjectReference 是 `Private=False`，不会自动拷贝，必须手工 `cp`。

### macOS 上 Dynamics 测试被什么挡住

引用链：

```
DWSIM.Automation.Dynamics.Tests
  → DWSIM.Automation
    → DWSIM.UI.Desktop
      → DWSIM.UI.Desktop.WPF   ← 需要 PresentationCore / PresentationFramework / WindowsBase / System.Xaml
```

Mono 没有 WPF。报错形如 `CS0246 ThemeInfoAttribute`、`CS0234 System.Windows.Controls`、`CS0012 FrameworkElement`。

`DWSIM.UI.Desktop.WPF` 只有 6 个源文件：

```
OxyPlot/PlotHandler.cs
Styling.cs
SkiaSharp/CanvasControl.cs
Properties/AssemblyInfo.cs
Properties/Resources.Designer.cs
Properties/Settings.Designer.cs
```

两条候选方案，**用户尚未选择，不要擅自决定**：

- **A. 桩程序集**：非 Windows 下用一个同名、同公开类型的假 `DWSIM.UI.Desktop.WPF` 工程替代真工程。
- **B. 条件排除**：非 Windows 下从 `DWSIM.Automation.csproj` 去掉 `DWSIM.UI.Desktop` 的 ProjectReference。

两者都要改上游工程文件；先问用户。改动建议只落在 `Directory.Build.targets` 或加 `Condition="'$(OS)'=='Windows_NT'"`，避免永久偏离上游。

## 3. 本轮已完成（都有 CI 证据）

| 改动 | 性质 | 提交 |
|---|---|---|
| ALK 电压模型重新标定（原 0.4 A/cm² 只有 1.467 V，低于热中性，等于 HHV >100%） | 物理错误 | `caf5c93ab` |
| Faraday 效率改为随载变化 `i/(i+i_cross)`，每类电解槽自带 `i_ref` | 模型缺陷 | `a79a74198` |
| 气体穿透随阴极压力（分压差）与温度（Arrhenius）缩放 | 模型缺陷 | `d4581b33e` |
| 穿透以两侧互换写入出口流股；`O2 in H2` / `H2 in O2` / `H2Purity` 改从流股读回，与组分 1e-9 一致 | 内部不一致 | `b6919f04e`、`1b271185b`、`ec8035959` |
| 功率低于电堆最小负荷时报错，不再静默钳位到 `i_min` | 静默失效 | `e5d3f6b7b` |
| 冷却水按模型 `CoolingDuty` 定容（PEM 5.80 kg/s、ALK 8.13 kg/s），替换猜的比例系数 | 参数错误 | `450b95603`、`b19de7b12` |
| 储氢罐用物性包的 `AUX_Z` 取真实压缩因子，固定 0.95 仅作无物性包时的回退 | 模型缺陷 | `5173f354b` |
| 纯化器新增 `Product Impurity`（原来 `Product Purity` 是 H₂ 含量 ppm，易被误读为杂质） | 单位/语义误导 | `fa8cb48f5` |
| `Cooler` 动态模型不再拒绝出口温度/温变/出口汽化率模式，按设定点计算 | 上游行为变更（见裁决） | `4b63854b4`、`c793d082e` |
| 厂模板重新生成（原模板 `ScheduleList` 为空，动态测试长期静默跳过） | 数据过期 | `349217fe6`、`b19de7b12` |
| PFR 样例 `CatalystVoidFraction=0` → 0.4（停留时间恒为 0，任何步长都不可能通过） | 上游样例数据缺陷 | `a37fa406e` |
| `RunOptions.StepSeconds` 真正传给积分器 | 实现缺陷 | `82f7374d0` |

### 新增回归

- 元素级（H/O 原子）物料守恒：闭合 1e-16
- 热中性基准能量守恒：闭合 0.0000%
- ALK 极化黄金数据 6 点（工业 30 wt% KOH / 80 °C 包络，最差 2.1%，并断言设计点电压高于热中性）
- 动态 6 样例数值基准：`tests/dynamic/samples_baseline.csv`，容差 1e-6，`H2SIM_WRITE_BASELINE=1` 可重生成
- 环境散热占比 1.59%（对照 Dinh 2021 的 2.6%，只做量级约束）
- 厂模板动态运行：ALK 314.15 K / 8.13 kg/s；PEM 310.74 K / 5.80 kg/s；均未跳车

## 4. 本轮的裁决（改之前先看，别推翻）

1. **改了上游 `Cooler.vb` 的动态行为**：原先对「出口温度 / 温变 / 出口汽化率」三种模式直接抛异常，现在按设定点计算。理由：这三种本质是受控出口状态，动态下可算；且定温冷却器是最常见配置。代价：配置了定温的冷却器在动态下会静默维持该温度，而不是提示配置问题。
2. **没有加 shunt-current 占比参数**：数值验证发现穿透损耗与 shunt 损耗都正比于电流，拆开对 `FaradayEfficiency` 没有任何影响，是个空旋钮，已回退未提交。
3. **没有数字化 Dinh 2021 热时间常数曲线**：`docs/h2-electrolyzer-retrofit-analysis.md` 只有定性结论（98.7% 精度、2.6% 散热占比），没有可数字化的数据，编造不可接受。
4. **V-5.1.7 的 Shadow 判据**用「同信息量偏差」（PLC 本步命令 vs 内部 PID 上一步输出），原始闭环偏差另列。原因：原始差值被 Modbus 一步零阶保持主导，不是控制律差异。
5. **Shadow / 联锁台架自建工况**：样例保存的初始状态是空罐，会把排液阀钉在限幅上，比较退化。

## 5. 未完成清单

### 用户当前最关心

- [ ] **macOS 本地跑通 `DWSIM.Automation.Dynamics.Tests`**：阻塞于 WPF，需用户在方案 A / B 中选一个。本轮所有模型回归都在这个工程里，目前只有 CI 证据。

### DWSIM 模型侧

- [ ] Dinh 2021 热时间常数曲线回归（缺原始数据）
- [ ] R2 气体穿透数值曲线（缺原始数据）
- [ ] ALK SEC 黄金数据
- [ ] 独立 G/L 闪蒸分离罐（目前只有 `H2Purification` 内的杂质去除，没有真正的闪蒸分离）
- [ ] 两级压缩的级间负荷目前用定温冷却器，压缩机效率固定 75% 等熵；可改为稳态后回填 `compressor.DeltaQ`

### 发布

- [ ] **升版本**：本轮有大量用户可见的模型行为变化，按 `AGENTS.md` 应升 `minor`（`bash scripts/version.sh minor`），并同步 `CHANGELOG.md`。目前仍是 `0.2.1`。
- [ ] macOS 交付（T-0.3 / `H2sim-<ver>-macos-x64.dmg`）：Gate 0 V-3 未验证，仍阻塞
- [ ] `PROJECTS.md` 登记 H2sim（T-6.1 遗留）

### 明确暂停

- [ ] V-5.1.8 / V-5.1.9 / V-5.1.10（PLC 相关）
- [ ] OPC UA `Basic256Sha256` + Username、断线重连、权限不足场景

## 6. 坑（别重复踩）

1. **C# 测试改动无法在本机验证**——除 Io 测试外，其他测试只有 CI 能跑。一轮 CI 约 10 分钟，改完必须读日志再下结论。
2. **CI 跑测试的工作目录是 `DistPackages\Windows`**。测试数据（`tests/electrolyzers/*.csv`、`tests/dynamic/*.csv`）必须在 `.csproj` 里用 `Link` + `CopyToOutputDirectory` 复制过去，否则找不到文件且只是打印"no reference"而不报错。
3. **`Automation3.CreateFlowsheet()` 返回 `Flowsheet2`（继承 `FlowsheetBase`），而经典 UI 的 `FormFlowsheet` 不继承 `FlowsheetBase`，自己另声明了一份 `DynamicsManager`**。改动态相关代码时注意用的是哪一份。
4. **厂模板改生成器后必须重新生成并提交 `.dwxmz`**，否则 CI 仍在测旧数据。
5. **`NetworkStream`/推送到 origin**：`origin` 的 URL 是 `git@github.com:...`（SSH 不可用），推送要加 `-c url."https://github.com/".insteadOf="git@github.com:"`。
6. **`gh` 默认仓库是 `DanWBR/dwsim`（上游）**，所有 `gh run` / `gh api` 必须带 `--repo humorwuwu/dwsim`。
7. **验证要读真实日志**，不要假设 PASS。之前有多次"测试通过"实际是因为分支被静默跳过。

## 7. 常用命令

```bash
# 看最新 CI
gh run list --repo humorwuwu/dwsim --limit 3 --json databaseId,status,headSha,displayTitle
gh run view <id> --repo humorwuwu/dwsim --json status,conclusion
gh run view <id> --repo humorwuwu/dwsim --log 2>&1 | grep -E 'FAIL:|error BC|purity |plant |energy '

# 推送
git -c url."https://github.com/".insteadOf="git@github.com:" push origin h2sim/phase-a

# 升版（交付前必做）
bash scripts/version.sh minor

# 阶段清理
bash scripts/clean.sh --dry-run
```
