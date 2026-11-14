# H2sim（DWSIM fork）改造为「电解水制氢工艺 + 控制仿真」专用平台的分析报告

- 仓库：`/Users/wcx/ongoing/H2sim`，origin `git@github.com:humorwuwu/dwsim.git`，upstream `https://github.com/DanWBR/dwsim.git`
- 分析基线：`windows` 分支，HEAD `1abf72d1b`（2026-07-17，`feat: scale the pump curves to the speed the pump runs at`）
- 许可证：GPLv3。所有改造必须继续以 GPLv3 开源发布
- 报告日期：本次会话

---

## 0. 结论摘要

1. **这个 fork 目前是纯上游镜像**，与 `upstream/windows` 差异为 0 个 commit（6213 个提交全部来自上游，`v9.0.5-69-g1abf72d1b`）。所以"改造"全部是新增工作，没有历史包袱，但也没有任何已有定制。
2. 全仓库只有一个电解槽模型：`DWSIM.UnitOperations/UnitOperations/CleanEnergies/WaterElectrolyzer.vb`（569 行）。它是**热力学反算型**模型（由 ΔG/ΔH 算可逆电压、热中性电压，再由 `Current = P/V` 直接除），**没有极化曲线、没有 Faraday 效率、没有气体穿透、没有热惯性、不支持动态仿真**。
3. 而 2024–2026 年用 DWSIM 做电解水制氢的论文（Bzovsky 2024、ICPEE 2025、EFCH2 2025 数据集）**全部绕开了内置 Water Electrolyzer**，自己写半经验电化学 UO。这不是巧合，是内置模型能力不足的直接证据：Bzovsky 2024 论文里明确写了"The 'Water Electrolyzer' model in DWSIM does not support dynamic simulation"。
4. 改造工作量评估：核心（P1+P2）约 5–7 人周，做完整"工艺 + 控制 + 自动化 API + 打包"约 13–17 人周。**不需要大改求解器架构**，但必须新增一批电解槽专用单元操作、补电解质物性、给动态框架加最小必要的求解能力。
5. 最高优先级的三件事：
   - 把电解槽从"热力学反算"改成"电化学极化模型 + 功率/电流闭合迭代 + Faraday 效率 + 气体穿透"；
   - 给电解槽实现 `SupportsDynamicMode` / `CreateDynamicProperties` / `RunDynamicModel`（stack 热惯性 + 冷却回路）；
   - 补齐 Alkaline 的 KOH 物性与 `PowerProfile`（功率/电流/电价时程）对象，并新增 `Automation3` 的时域仿真 API。

---

## 1. 仓库现状清点

### 1.1 技术栈

| 项 | 值 |
|---|---|
| 语言 | VB.NET 为主（UnitOperations/Thermodynamics/FlowsheetSolver 等核心），C# 为辅（`DWSIM.UI.Desktop.*`、`DWSIM.Automation`、`DWSIM.ProFeatures`） |
| 目标框架 | .NET Framework `v4.6.2`（全部项目统一） |
| 解决方案 | `DWSIM.sln`，68 个项目，71 个顶层目录 |
| 多平台 | Classic UI（WinForms，功能最全）+ Cross-Platform UI（Eto.Forms，macOS/Linux）+ GTK/GTK3/WPF/Mac 各壳 |
| 构建配置 | `Debug|x64`、`ReleaseLinux|*`、`ReleaseWinMac|*`、`ReleaseWinMacP|*` |
| 版本 | `AssemblyVersion 9.0.5.0` 硬编码在各 `My Project/AssemblyInfo.vb`；**无 `VERSION` 文件** |
| 自动化 | `DWSIM.Automation`（`Automation3`）、`DWSIM.Apps.TCPServer`（分布式求解）、`DWSIM.Apps.AzureServer` |
| 本机环境 | 当前 macOS 上没有 `dotnet` / `mono` / `msbuild`，**不能在本机构建**（需先解决构建环境） |

### 1.2 与电解水直接相关的代码

| 文件 | 行数 | 内容 |
|---|---|---|
| `DWSIM.UnitOperations/UnitOperations/CleanEnergies/WaterElectrolyzer.vb` | 569 | 唯一电解槽模型 |
| `DWSIM.UnitOperations/UnitOperations/CleanEnergies/CleanEnergyUnitOpBase.vb` | 64 | 电解槽/风机/光伏共基类，`IsSource = True`，`ObjectClass = CleanPowerSources` |
| `DWSIM.UnitOperations/UnitOperations/CleanEnergies/PEMFuelCellUnitOpBase.vb` + `PEMFC_Amphlett/ChamberlineKim/LarminieDicks.vb` | 392+311+54+68 | 燃料电池（**反向过程**，通过 OPEM python 包计算），可复用其"输入参数字典 + 曲线"UI 范式 |
| `DWSIM.UnitOperations/EditingForms/EditingForm_WaterElectrolyzer.vb(.Designer)` | — | 经典 UI 编辑窗体（连接关系 + 3 个输入 + 7 个输出） |
| `DWSIM.Thermodynamics/PropertyPackages/ElectrolyteBase.vb`、`ElectrolyteIdeal.vb`、`LIQUAC2PropertyPackage.vb` | — | 电解质物性包（`Ideal Solution (Aqueous Electrolytes)`、LIQUAC2、Reaktoro） |
| `DWSIM.Thermodynamics/Assets/Databases/electrolyte.xml` | 84 个物种 | 有 `Hydroxide`、`Potassium Ion`、`Sodium Hydroxide`；**没有 Potassium Hydroxide** |
| `DWSIM.Thermodynamics/Assets/henry_constants.csv` | — | 有 O2、H2 的 Henry 常数（气体穿透模型可用） |
| `PlatformFiles/Common/samples/Water Electrolyzer.dwxmz` | — | 官方示例：Water/Hydrogen/Oxygen + Raoult's Law + SolarPanel，稳态，`SupportsDynamicMode=false` |
| `DWSIM.Interfaces/Enums.vb` | — | `ObjectType.WaterElectrolyzer`（L745）、`ObjectType.PEMFuelCell`、`SimulationObjectClass.CleanPowerSources=17`、`SimulationObjectClass.Electrolyzers=18`（**枚举存在但没有类用它**） |

### 1.3 现有 WaterElectrolyzer 的计算逻辑（逐条对照需求）

`Calculate()`（L394–545）实际做的事：

```
ΔG = pp.AUX_DELGig_RT(298.15, T, {water, H2, O2}, {-1,+1,+0.5})
ΔH = pp.AUX_DELHig_RT(...) + ΔHvap(water)
     [再用手写 NIST Cp 积分多项式修正 ΔG = ΔH + T·(S_water - 0.5·S_O2 - S_H2)]
V_rev = ΔG/(2F),  V_tn = ΔH/(2F)

分支 A：Voltage>0 且 NumberOfCells>0
    I   = EnergyFlow[kW]·1000 / Voltage          ← 直接相除，无极化曲线
    n_e = I/F · Ncells
    n_H2 = n_e/4·2,  n_O2 = n_e/4,  n_H2O_react = n_e/4·2
    CellVoltage = Voltage/Ncells
    if CellVoltage < V_rev: 抛异常
    WasteHeat = (CellVoltage - V_tn)·I·Ncells/1000    [kW]

分支 B：InputEfficiency ∈ (0,1]
    WasteHeat = (1-η)·P；n_H2 = η·P/ΔH
    CellVoltage = V_tn/η，Current = 0，Voltage = 0

Efficiency = (P - WasteHeat)/P      ← 实为 U_tn/U_cell，HHV 基准

出口 1（H2-rich）：PT flash 得饱和水蒸气（x_H2O = Pvap(T)/P），再按 PH 加上 WasteHeat·(m1/m_in)
出口 2（O2-rich）：剩余全部水 + O2，同样 PT→PH
```

**由此得到的明确缺陷清单：**

| 编号 | 缺陷 | 影响 |
|---|---|---|
| W1 | 无极化曲线，`I = P/V` 线性 | 无法给部分负荷效率、无法算 SEC(kWh/kg)、无法做"最优功率点"分析；文献 1/3 的核心结论全部做不出来 |
| W2 | 单电压标量，无 `U_act / U_ohm / U_conc` 分解 | 无法用膜厚、温度、λ（水合度）、电极过电位做参数研究 |
| W3 | Faraday 效率恒为 100% | H2 产量偏高。文献（Dinh 2021）实测工业 ALK 额定点仅 86%，主因 shunt current；低载气体穿透更严重 |
| W4 | 无气体穿透（crossover） | 无法判断 H2-in-O2 是否越过 4% 爆炸下限——**这是低载/启停运行最关键的约束**（文献 3） |
| W5 | **H2 侧与 O2 侧用同一个压力 P** | 文献标准配置是阴极 30 bar / 阳极 2 bar，压差是膜的机械工况和安全判据 |
| W6 | 无热惯性、无冷却回路 | `RunDynamicModel` 走基类 → 抛 `"This Unit Operation is not yet supported in Dynamic Mode."`（`SimulationObjectBaseClasses.vb` L359）。动态模式直接不可用 |
| W7 | `WasteHeat` 按质量流比例均分到两股出口 | 简化可接受，但没有"冷却水带走的热"这条独立能量流，无法把冷却回路、板换、余热回收接起来 |
| W8 | `EquipmentTypes = {"", "PEM", "Alkaline", "Solid Oxide"}` 只是 UI 字符串，`Calculate()` 完全不读 | 选 Alkaline 也走 PEM 的热力学逻辑；ALK 的 KOH 浓度、电导率、分离罐全部缺失 |
| W9 | 无衰减（电压衰减 vs 启停次数/低载时长） | 无法做 LCOH 敏感性与寿命评估 |
| W10 | 无 `Inspector` 段落（对比 `Splitter.vb` L203 有完整 Inspector 记录） | 计算不可审计、不可追踪 |
| W11 | `GetPropertyUnit` 里 `Case Else → Return 0.0`（String 函数返回 Double，靠 `OptionStrict Off` 混过） | 潜在运行时 `InvalidCastException`，是真实 bug |
| W12 | 错误信息、属性名全部硬编码英文，未走 `GetTranslatedString` | 中文化/国际化不可行 |

### 1.4 动态与控制框架现状

| 能力 | 现状 | 位置 |
|---|---|---|
| 积分器 | 固定步长显式积分；`IntegrationStep` 默认 5 s，`CalculationRateControl/Equilibrium/PressureFlow` 三个子采样率 | `DWSIM.DynamicsManager/Integrator.vb` |
| 动态 UO 契约 | `SupportsDynamicMode`、`CreateDynamicProperties`、`RunDynamicModel`，**各 UO 自己用 timestep 做积分** | `ISimulationObject.vb`、`SimulationObjectBaseClasses.vb` |
| 存量法 | `AccumulationStream`（`Vessel`/`Tank` 用 `ims.Subtract(oms, timestep)`） | `UnitOperations/Vessel.vb` L357+ |
| 控制 | `PIDController`（Kp/Ki/Kd、windup guard、output min/max、reverse acting、PV/MV/SP 历史曲线）、`PythonController`（IronPython 脚本） | `UnitOperations/Controllers/` |
| 事件 | `Event`/`EventSet`/`Schedule`/`CauseAndEffectMatrix`，支持 Step/Linear/Log/InverseLog/Random 变化 | `DWSIM.DynamicsManager/` |
| 时序驱动 | 靠 `DynamicsManager.RunSchedule` + EventSet，没有"功率曲线"这类一等公民对象 | Bzovsky 2024 就是这样手工调功率的 |
| 优化 | `SensitivityAnalysisCase`、`OptimizationCase`（IPOPT / L-BFGS-B / 截断牛顿 / Simplex） | `DWSIM.SharedClasses/Flowsheet/`、`DWSIM.Math/` |
| 求解顺序 | `IsSource=True` 的 UO 放进 `lists(0)`（最先算）；动态模式下"无连接但 `SupportsDynamicMode`"的 op 追加到 `objstack` | `FlowsheetSolver.vb` L1030、L1091–1103 |
| 工业接口 | **无 OPC UA / Modbus / DDS**（全仓库 0 处匹配） | — |
| 自动化 API | `Automation3.LoadFlowsheet / CalculateFlowsheet / CreateFlowsheet / SaveFlowsheet`；**无动态步进 API** | `DWSIM.Automation/Automation.cs` |

---

## 2. 文献调研：用 DWSIM 做水电解槽制氢的论文

### 2.1 直接使用 DWSIM 的工作（核心参考）

| # | 文献 | 关键内容（对本项目有用的部分） |
|---|---|---|
| **L1** | **Bzovsky I., Kudriavtsev S., "Modeling of a PEM Electrolyzer Operating at Part Load Conditions for Dynamic Simulation in DWSIM", *Periodica Polytechnica Chem Eng* 68(4), 655–664, 2024. DOI `10.3311/PPch.38170`**（开放获取，已下载全文） | ① 明确指出 DWSIM 内置 Water Electrolyzer **不支持动态仿真**，因此自建 "PEM Electrolyzer" 自定义 UO；② 给出完整半经验模型：Nernst 开路电压 + 简化 Butler-Volmer（只算阳极，`i0,an = 2.16×10⁶ A/cm²`、`E_act,an = 76 kJ/mol`）+ 欧姆（电极 ρ=7.5 mΩ·cm、t=1 mm；膜电导 Springer 式、λ=22）+ 浓差（`i_L = 6 A/cm²`、α=0.5）；③ **关键的工程技巧**：把"功率→电流"用 `I = aP² + bT² + cPT + dP + eT + f` 拟合，最大归一化电流误差 < 0.8%，避免在 UO 内做迭代；④ BoP 全部用标准块（TANK-1、PUMP-1、CL-1 冷却器、SEP-H2、SEP-O2/V-2）+ PID 控制温度/压力/液位；⑤ 功率用 `DynamicsManager` EventSet 调度；⑥ 用 SLSQP 优化负荷；⑦ 平台参数：1 MW、182 片、1000 cm²、i_max 2.5 A/cm²、V_cell,max 2.2 V、P_cat 30 bar、P_an 2 bar、60–80 °C、给水 32040 kg/h、纯度 99.9%；⑧ 自述局限：稳态电化学 + 不含热惯性、不含衰减、不含给水净化/预热 |
| **L2** | "Simulation of Alkaline Water Electrolysis in DWSIM: An Open-Source Comparison with Aspen-Based Results", *ICPEE 2025*. DOI `10.1109/icpee67608.2025.11422151` | ① 在 DWSIM 里实现半经验电压式 + **"功率控制的迭代求解器"反求电流密度**（与 L1 的拟合式是两条不同技术路线，本项目应两者都支持）；② 与 Aspen Plus 结果对比，KPI：cell voltage、thermal efficiency、system efficiency、**Faraday efficiency**、**specific energy consumption**；③ 结论：两平台高度一致，DWSIM 可作开源替代 |
| **L3** | Bianchi F.R., Bosio B., "High- and low-temperature electrolysis plant design in DWSIM", EFCH2 2025 数据集. DOI `10.5281/zenodo.18410564`（含 PEMEC/SOEC 两套 plant PDF + xlsx） | 完整的**厂级 flowsheet 拓扑**：PEMEC 块 + 阴极循环 + G/L 分离 + H2 纯化 + 两级压缩（90 bar → 250 bar）+ 水净化 + 多个 E_MIX/E_SPL + 阀门。是"BoP 参考拓扑"的直接模板 |
| **L4** | "Green hydrogen for sustainable ammonia production: PEM electrolysis powered by wind energy", *IOP Conf. Ser. EES* 1491 012037, 2025. DOI `10.1088/1755-1315/1491/1/012037` | 用 DWSIM 电解槽 + **逐时气象数据**驱动，输出波动风光下的产氢曲线——正好对应 `DWSIM.SharedClasses/Weather` 现有基础设施 |
| **L5** | "Techno-Economic Assessment of Hydrogen Integration for Decarbonizing the Steel Industry: A Case Study", *Hydrogen* 6(4) 104, 2025. DOI `10.3390/hydrogen6040104` | 用 DWSIM **v9.0.2** 做水电解制氢供氢、算 CO2 减排与成本——说明"DWSIM + 外部 Excel 做经济性"是当前社区的通行做法（也说明开源侧缺 LCOH 工具） |
| **L6** | "A methodological simulation framework for implementing a detailed kinetic model using DWSIM for dry reforming" / "Simulation and optimization of hydrogen production from biogas by integration of DWSIM and AI", *IJHE* 2026. DOI `10.1016/j.ijhydene.2026.156383`、`10.1016/j.ijhydene.2026.156849` | 非电解路线，但示范了"在 DWSIM 里塞自定义动力学模型 + 外部 AI/优化器闭环"的工程模式，可借用其 UO 与外部求解器的耦合方式 |

### 2.2 必须参照、但不是 DWSIM 的模型文献（补齐物理）

| # | 文献 | 对改造的约束 |
|---|---|---|
| **R1** | Dinh et al., "Dynamic energy and mass balance model for an industrial alkaline water electrolyzer plant process", *IJHE* 2021. DOI `10.1016/j.ijhydene.2021.11.126`（245 次引用） | 3 MW / 16 bar 工业 ALK 的 0-D 动态模型（MATLAB/Simulink）。**Faraday 效率 86% 的主因是 shunt current**；stack 对环境散热只占总损失的 2.6%；热模型精度 98.7%。→ 本项目必须能表达 ηF 和 stack 散热 |
| **R2** | "Transient modelling of a multi-cell alkaline electrolyzer for gas crossover and safe system operation", *IJHE* 2023. DOI `10.1016/j.ijhydene.2023.05.184` | 低载时**气体穿透主导**，H2-in-O2 超过 4% 即形成爆炸气氛；分离罐尺寸决定杂质累积速率；支持混合/分离/动态切换电解液循环。→ 安全约束建模依据 |
| **R3** | "Differential algebraic modeling of an alkaline electrolyzer plant", *ECC* 2025. DOI `10.23919/ecc65951.2025.11186941` | 用 DAE 形式统一建模 stack + 水循环 + 储氢 + 压缩机，用于"模型基优化与控制"。→ 长期若要 MPC/动态优化，需要 DAE 而非纯显式 |
| **R4** | "Thermodynamical and electrochemical model of a PEM electrolyzer plant in the megawatt range…", *IJHE* 2024. DOI `10.1016/j.ijhydene.2024.04.335` | **交换电流密度 i0 在文献中相差 10⁹ 倍**，该文提出用辅助参数 + 阳极/阴极约束来标定。→ 本项目参数库必须"可标定 + 可溯源"，不能硬编码一套 |
| **R5** | "Control and control-oriented modeling of PEM water electrolyzers: A review", *IJHE* 2023. DOI `10.1016/j.ijhydene.2023.04.204` | 控制导向模型的分类与最小状态集；列出压力/温度/液位/纯度四类关键回路 |
| **R6** | "An Optimized Balance of Plant for a Medium-Size PEM Electrolyzer: Design, Control and Physical Implementation", *Electronics* 9(5) 871, 2020. DOI `10.3390/electronics9050871` | BoP 三大子系统（电源、水管理、氢气处理）的划分与控制实现，可做 BoP 对象清单依据 |
| **R7** | "Design considerations for industrial water electrolyzer plants", *IJHE* 2021. DOI `10.1016/j.ijhydene.2021.09.018` | 碱液循环流量（变流量比定流量多产氢 8–12%）、冷却能力按 EoL 衰减堆设计。→ 冷却回路模型必须支持变流量 |
| **R8** | "Implementation of a Semi-Empirical Model for a Low-Temperature Alkaline Electrolyzer in Aspen HYSYS®", 2023. DOI `10.52202/069564-0099` | 在商业流程模拟器里"用标准块 + 自定义 spreadsheet 实现半经验式，非线性回归标定"。→ 证明了"流程模拟器 + 半经验电化学"是可行范式，也是 DWSIM 应当内置的东西 |
| **R9** | "A Comprehensive Survey of Alkaline Electrolyzer Modeling: Electrical Domain and Specific Electrolyte Conductivity", *Energies* 15(9) 3452, 2022. DOI `10.3390/en15093452` | ALK 性能**由电解液浓度与温度决定**；给出电导率关联式汇编。→ KOH 物性/电导率的参数来源 |
| **R10** | "Electrolyzer modeling and real-time control for optimized production of hydrogen gas", *Applied Energy* 2020. DOI `10.1016/j.apenergy.2020.116031` | 实测 100 kW PEM（Siemens SILYZER 100）标定 + 滚动时域最优控制 + 储罐套利。→ 控制目标的现实基线（成本最小化而非效率最大化） |

### 2.3 从文献归纳出的"必备功能集"

按出现频率排序（几乎每篇都要）：

1. 极化曲线（U–i）随温度、压力、电解液浓度变化
2. 部分负荷（10–100%）行为与最低负荷约束
3. Faraday 效率 / 气体纯度 / H2-in-O2
4. 比能耗 SEC（kWh/kg H2）与效率（HHV/LHV 两种基准都要）
5. stack 热平衡 + 冷却回路（变流量）
6. 分离罐（H2/O2）液位与压力控制
7. 功率/电流时程（风光出力、电价）
8. 产品气纯化 + 压缩 + 储存
9. 经济性（CAPEX/OPEX/LCOH）

---

## 3. 差距矩阵：需求 vs 现状

| 需求（来自 §2.3） | 现有 DWSIM 能力 | 缺口等级 |
|---|---|---|
| 极化曲线 | 无 | **致命** |
| 部分负荷 + 最低负荷 | 无（只有"V_cell < V_rev 就抛异常"） | **致命** |
| Faraday 效率 | 恒 100% | **致命** |
| 气体穿透 / 纯度 / 爆炸下限 | 无 | **致命** |
| SEC、HHV/LHV 效率 | 只有 `Efficiency=(P-WH)/P` | 高 |
| stack 热惯性、冷却回路 | 无（`SupportsDynamicMode=False`） | **致命** |
| 分离罐液位/压力控制 | 有（`Vessel`/`Tank` + `PIDController` + `LevelGauge`） | 无 |
| 功率/电流时程 | 靠 EventSet 手工搭 | 高 |
| 风光驱动 | `WeatherData`/`WeatherProvider` 已有 | 低 |
| 纯化（TSA/PSA/DeOxo） | 无吸附/干燥 UO | 高 |
| 压缩/储存 | 有 `Compressor`（多级可串）、`Vessel`/`Tank` | 低 |
| 阴极/阳极独立压力 | **无**（共用 P） | 高 |
| ALK 电解液（KOH）物性 | 化合物库无 KOH；有 `Hydroxide`/`Potassium Ion` | **致命**（对 ALK） |
| 电解质电导率 | 无此物性字段 | 高 |
| SOEC（高温共电解） | 无 | 中（可后置） |
| 衰减模型 | 无 | 中 |
| 经济性 / LCOH | 在闭源 `DWSIM.ProFeatures` | 高（若要交付完整方案） |
| 动态仿真 API | 无 | 高（若要脚本化控制研究） |
| OPC UA / Modbus | 无 | 中（若要对接真实 PLC） |
| 回归测试 / 基准 | `DWSIM.Tests` 只跑通不校验数值 | 高 |

---

## 4. 改造方案

### 4.0 总体策略（重要）

- **不要就地改 `WaterElectrolyzer.Calculate`**。它被 `ObjectType.WaterElectrolyzer` 和官方示例 flowsheet 引用，改坏会破坏兼容性。把它标记 `Obsolete` 但保留，作为 legacy。
- **新增一组电解槽 UO**，走 `IExternalUnitOperation` + `ObjectType.External` 路线。理由：现有 `WaterElectrolyzer` 本身就是 `IExternalUnitOperation`，靠 `SharedClasses.Utility.LoadAdditionalUnitOperations()` 反射发现（`DWSIM/Forms/MainWindow/FormMain.vb` L936、`FlowsheetBase.vb` L3608）；新增一个类**只需在 `.vbproj` 加编译项 + 资源**，不必改 `Enums.vb`、`FlowsheetBase.AddObjectToSurface`、`FlowsheetSurface_SkiaSharp`、`FormFlowsheet`、`SimulationObjectsPanel` 等 5–7 处。对比：新增 `ObjectType` 枚举值要同步改 7 处以上。
- 把电解槽相关代码集中到新目录 `DWSIM.UnitOperations/UnitOperations/Electrolyzers/`，与 `CleanEnergies/` 平级，避免污染上游目录、减少 future merge 冲突。

### 4.1 P1：电解槽电化学模型（核心，3–4 人周）

新建文件：

```
DWSIM.UnitOperations/UnitOperations/Electrolyzers/
├── ElectrolyzerUnitOpBase.vb        # 公共：连接器、参数容器、报告、UI 面板、序列化
├── ElectrochemicalModelBase.vb      # 电压模型接口 + 参数定义（Newton 求解 U-i）
├── ModelPEM_Springer.vb             # PEM 半经验（L1 参数）
├── ModelAlkaline_CurrentDensity.vb  # ALK 半经验 + U-i 耦合（R9 电导率式）
├── ModelSOEC_HighTemp.vb            # （P5 再做）
├── PEMElectrolyzer.vb               # 组装 PEM
├── AlkalineElectrolyzer.vb          # 组装 ALK
└── ElectrolyzerCalculator.vb        # 功率↔电流闭合、Faraday、crossover、SEC 等 KPI
```

**计算流程（每个 UO 的 `Calculate()`）**

```
1. 读入：P_stack[kW]（EnergyStream）或 I_stack[A]（模式二选一）
         T_stack、P_cat、P_an、T_feed、m_feed、电解液浓度/循环流量
2. 由 T、P 算 Nernst 开路电压 U_ocv（用现有 pp.AUX_DELGig_RT + Nernst 项 ln(p_H2·√p_O2 / a_H2O)）
3. 内层迭代求电流（关键，替代现状的 I=P/V）：
     U_cell(i) = U_ocv + U_act(i,T) + U_ohm(i,T,λ) + U_conc(i,T)
     P_calc(i) = U_cell(i) · i · A_cell · N_cells
     用 Brent / 二分（DWSIM.Math/Brent.vb 已有）解 P_calc(i) = P_target
     区间 [i_min, i_max]，i_min 由"最低负荷"给定（默认 10% 额定），不收敛则报可诊断的错误
     备选（L1 路线）：用预先拟合的 I = f(P,T) 多项式，避免迭代，作为 UI 里可选"快速模式"
4. Faraday 效率：ηF = f(i, T, ΔP, 电解液循环)  →  n_H2 = ηF · i·A·N/(2F)
   气体穿透：n_H2,cross = k_perm(T, ΔP) / i   （低载时相对影响放大）
5. 物料：H2 出口（H2 + 饱和水 + 微量 O2）；O2 出口（O2 + 饱和水 + 微量 H2 + 过量循环水）
   两侧用**各自的压力**（P_cat / P_an）做 flash —— 修正 W5
6. 能量：Q_gen = N·i·A·(U_cell - U_tn)  [kW]
   动态模式：dT/dt = (Q_gen - Q_cool - Q_loss) / (m_stack·Cp_stack)
   冷却：Q_cool = m_cw·Cp·(T_out - T_in)，或 lye loop 的 m_lye·Cp_lye·ΔT + 板换
   稳态模式：Q_cool 由能量流输出（新增一条出口能量流）或由目标 T 反算
7. KPI 输出（全部进 GetProperties + GetReport + Inspector）：
   CellVoltage, CurrentDensity, StackVoltage, Current, Power,
   ReversibleVoltage, ThermoneutralVoltage, U_act, U_ohm, U_conc,
   FaradayEfficiency, H2Purity, H2inO2_pct, O2inH2_pct,
   SEC_kWh_per_kg_H2, Efficiency_HHV, Efficiency_LHV,
   CoolingDuty, StackTemperature
```

**必须新增的属性/单位**：电解槽需要 `[V]`、`[A]`、`[A/cm²]`、`[kWh/kg]` 这些单位，而 `IUnitsOfMeasure`/`SystemsOfUnits.vb` 里**没有 voltage / current / currentDensity**（只有 `specific_power`）。两个选项：
- 最小侵入：UO 内部全部用 SI，`GetPropertyUnit` 直接返回字面 `"V"`,`"A"`,`"A/cm²"`（现有 `WaterElectrolyzer` 就是这么干的）；
- 规范做法：在 `DWSIM.Interfaces/Enums.vb` 的 `UnitOfMeasure` 追加 `voltage=72, current=73, currentDensity=74`，在 `SystemsOfUnits.vb` 的 6 个单位制类里各加属性（SI 全用 SI 单位即可，其余可简单换算）。改动约 120 行但一次到位。**建议做规范做法**，因为后续要接 OPC UA / PLC 单位时省事。

**参数化（学 PEMFC 的 `InputParameters` 字典范式）**

`PEMFuelCellModelParameter`（`Name/Description/Value/Units/ValuesX/ValuesY` 支持曲线）已存在，直接复用为 `ElectrolyzerModelParameter`，把 L1 的全部参数做成可编辑：

```
A_cell, N_cells, i_max, V_cell_max, i_min_frac,
i0_an_ref, E_act_an, alpha_an, i_L, alpha_conc,
t_mem, lambda_mem, rho_electrode, t_electrode,
P_cat, P_an, T_op, etaF_ref, etaF_curve, crossover_coeff,
C_stack, m_stack, Cp_stack, U_loss_area, cooling_mode
```

并提供**参数集预设**（PEM-Alkaline-Siemens Silyzer 300 近似值 / ALK 3 MW 近似值 / L1 的 1 MW 案例），来源写进 `docs/parameters/*.md`，符合 R4 的"可标定 + 可溯源"要求。

### 4.2 P1b：动态支持（含在 P1 内，1 人周）

```vb
' PEMElectrolyzer.vb / AlkalineElectrolyzer.vb
Public Overrides ReadOnly Property SupportsDynamicMode As Boolean = True
Public Overrides ReadOnly Property HasPropertiesForDynamicMode As Boolean = True

Public Overrides Sub CreateDynamicProperties()
    AddDynamicProperty("Stack Temperature", "Current stack temperature", 353.15, UnitOfMeasure.temperature, 1.0.GetType())
    AddDynamicProperty("Cooling Water Flow", "...", 0, UnitOfMeasure.massflow, ...)
    AddDynamicProperty("Coolant Inlet Temperature", "...", 298.15, ...)
    AddDynamicProperty("H2 in O2", "Vol% H2 in oxygen stream", 0, UnitOfMeasure.none, ...)
    AddDynamicProperty("Enable Thermal Model", "...", True, UnitOfMeasure.none, True.GetType())
End Sub

Public Overrides Sub RunDynamicModel()
    Dim timestep = ... ' 照 Vessel.vb L202 的取法：DynamicManager → CurrentSchedule → Integrator → IntegrationStep
    ' 半隐式欧拉：T_new = (C·T_old + dt·(Q_gen + Q_loss_in) ) / (C + dt·(U·A + m_cw·Cp_cw))
    ' 收敛后写回 Streams、更新 dynamic properties
End Sub
```

再定义 `DynamicsSpec`：电解槽作为"有压力降的两端口"应支持 `Pressure`；但注意现在 `GetSolvingList` 会把 `IsSource=True` 的 UO 放到 `lists(0)` 最先算——电解槽有入口物料流、不是真 source，**建议新 UO 的 `IsSource` 返回 `False`**（现状 `CleanEnergyUnitOpBase.IsSource=True` 是给风机/光伏用的，电解槽继承它是错的，会导致计算顺序异常）。

⚠️ 现有动态框架是"每个 UO 自己积分、固定步长显式"。电解槽热惯性很小（C_stack 大约 10²–10³ kJ/K 量级，与 kW 级 Q_gen 匹配），若用户把 `IntegrationStep` 设成 60 s 显式积分会发散。**对策**：`RunDynamicModel` 内部做子步（例如 dt_sub = min(timestep, τ/10)，τ = C/(UA + mCp)），这是最小改动且不触碰框架。长期（P5）可考虑引入集中式 ODE/DAE 求解器（对齐 R3），但那是大改。

### 4.3 P2：物性与化合物（2 人周）

1. **KOH / NaOH**：`electrolyte.xml` 里加 `Potassium Hydroxide`（含 `DelGF/DelHF/Cp0`、`IsIon=false`、`PositiveIon=Potassium Ion`、`NegativeIon=Hydroxide`、电解质相关字段，参照已有 `Sodium Hydroxide` 条目）。同时补 `K2CO3`（ALK 电解液吸收 CO2 后生成，是长周期杂质）。
2. **电解液电导率**：`ICompoundConstantProperties` 无电导率字段。建议**不改物性框架**，而是在 `ModelAlkaline_CurrentDensity.vb` 里内置 Gilliam 式/σ(c,T) 关联式，把质量分数与温度作为输入，这样不影响热力学计算，风险最低。
3. **H2/O2 溶解**：`henry_constants.csv` 已有数据，`AUX_KHenry` 可用（`ElectrolyteIdeal.vb` L573 已在用）。把溶解量纳入 H2 收率损失。
4. **高温侧**：SOEC 需要 `SteamTablesPropertyPackage`（IAPWS-97，已有）+ 高温 Nernst，P5 再做。

### 4.4 P3：BoP 对象与流程模板（3–4 人周）

| 需要的东西 | 现状 | 做法 |
|---|---|---|
| 气液分离罐（H2/O2） | `Vessel`（动态、`AccumulationStream`）已有 | 直接复用，做分离罐模板 + 液位 PID 示例 |
| 电解液循环泵 + 板换 | `Pump`（含曲线、变速）、`HeatExchanger` 已有 | 直接复用 |
| **H2 纯化（TSA/PSA/DeOxo）** | 无 | 新 UO `H2Purification.vb`：以"去除 O2/H2O 到目标 ppm + 再生气耗 + 压降"的黑箱模型起步（不要一上来做严格吸附） |
| **H2 储罐/管束** | `Vessel`/`Tank` 可用但无专用模型 | 新 UO `HydrogenStorage.vb`：P-V-T（PR 或简化 Z）+ 充装/取用 + 压力等级 |
| 多级压缩 | `Compressor` 已有 | 串两级（90 bar → 250 bar，照 L3） |
| 水净化/去离子 | 无 | 黑箱 UO 或直接作为给水规格约束（按 L1 的自述局限，这个可以不做） |
| **PEM / ALK 全流程模板** | 只有稳态 `Water Electrolyzer.dwxmz` | 在 `PlatformFiles/Common/samples/` 加两个示例：`PEM Electrolysis Plant.dwxmz`、`Alkaline Electrolysis Plant.dwxmz`，参数取 L1（1 MW/182 cells）与 R1（3 MW/16 bar） |

### 4.5 P4：控制、时序与自动化（3 人周）

1. **`PowerProfile` 一等公民对象**（新 UO，`ObjectClass=Inputs`）：输出一条 EnergyStream 的功率时程，支持
   - 手工点表（t, P）
   - 从 CSV 读（风光出力/电价曲线，接 L4）
   - 从 `WeatherData` 实时推（`DWSIM.SharedClasses/Weather`）
   - 作为 `Schedule`/`EventSet` 的替代，解决"功率曲线只能靠 EventSet 堆"的问题
2. **PID 增强**（`Controllers/PIDController.vb`，约 +120 行）：输出速率限制、bumpless transfer（手动↔自动）、积分分离、设定值斜坡；这些是 DCS 常规功能，论文里的温度/压力/液位回路都需要。
3. **安全连锁**：不新增对象，用已有 `CauseAndEffectMatrix` + 报警模板：
   - `H2-in-O2 > 2%` → 高报，降负荷
   - `H2-in-O2 > 4%`（LEL）→ 跳车（切断功率流）
   - `P_cat - P_an > 允许压差` → 跳车
   在 docs 里写清逻辑，示例 flowsheet 里搭好。
4. **`Automation3` 时域仿真 API**（`DWSIM.Automation/Automation.cs`，约 +200 行）：
   ```csharp
   public void RunDynamics(IFlowsheet fs, double durationSeconds, double stepSeconds);
   public void SetPropertyAt(IFlowsheet fs, string objectTag, string prop, object value);
   public Dictionary<string,double> GetResults(IFlowsheet fs);
   public void InjectEvent(IFlowsheet fs, ...);
   ```
   实现方式：内部驱动 `DynamicsManager` + `FlowsheetSolver.SolveFlowsheet`，参考 `FormDynamicsIntegratorControl.vb` L430–520 的循环（含 `ShouldCalculateControl/Equilibrium/PressureFlow` 子采样逻辑），但去掉 UI 依赖（`Flowsheet2.cs` 已有"无 UI 的 FlowsheetBase 实现"，可作为宿体）。
5. **（可选，P6）OPC UA 桥**：若要"半实物"验证，加一个 `DWSIM.Apps.OpcUaBridge`（基于 `Opc.UaFx` 或 `Workstation.UaClient` 包），把 UO 属性 tag 映射到 OPC 节点。工作量 2 周，非必需。

### 4.6 P5：验证与回归（持续，1–2 人周）

建立 `tests/electrolyzers/` 下 4 组黄金数据：

| 测试 | 数据来源 | 判据 |
|---|---|---|
| 极化曲线 | L1 Table 4 参数 + 图 2；R9 电导率式 | U_cell 相对误差 < 2% |
| 部分负荷 / SEC | L1 图 5；L2 的 KPI 表 | SEC 误差 < 3% |
| 稳态/动态热行为 | R1（3 MW ALK，热模型 98.7%）；L1 的 Tank/Sep 尺寸 | 稳态温度 < 1 K；阶跃响应时间常数 < 10% |
| 气体纯度 / H2-in-O2 | R2（4% LEL 曲线）；L1（99.9% 纯度） | 趋势一致 + LEL 交点位置误差 < 10% |

注意：现有 `DWSIM.Tests/Program.cs` 只遍历 `samples/*.dwxmz` 判断"不抛异常"，**不校验数值**。需要新增数值断言（可用简单 console assert，不必引入 NUnit；`DWSIM.Automation.Tests.CSharp` 已是 console 风格）。

### 4.7 P6：打包与工程化（1 人周）

按工作区规约（`AGENTS.md`）：

- 建 `VERSION`（唯一版本来源）+ `scripts/build.sh` / `scripts/clean.sh`（支持 `--dry-run`/`--deep`）+ `docs/`
- 产物：`release/macos/H2sim-<ver>-macos-arm64.dmg`（内含 `.app`）、`release/windows/H2sim-<ver>-windows-x64.exe`（单文件绿色版）
- `release/`、`build/`、`bin/`、`obj/`、`packages/`、`.vs/` 进 `.gitignore`
- **H2sim 目前还没登记到工作区 `PROJECTS.md`，也没有项目自己的 `AGENTS.md`/`CLAUDE.md`**。按规约需要补（目录名 `H2sim` 含大写字母，与"全小写 ASCII"规约冲突，建议登记时一并说明为上游派生仓库的例外，或改为 `h2-sim`）
- 版本策略建议：不跟上游 `9.0.5` 拼号，用自己的 `0.1.0` 起，但保留 `UPSTREAM_BASE=<commit>` 记录以便回溯

---

## 5. 工作量与优先级

| 阶段 | 内容 | 人周 | 依赖 |
|---|---|---|---|
| P0 | 构建环境打通（Windows/CI，或容器内 mono/msbuild）、跑通现有 samples、取黄金数据 | 1 | — |
| P1 | 电解槽电化学模型 + 功率电流闭合 + KPI + 单位体系扩展 | 3–4 | P0 |
| P1b | 动态支持（热惯性 + 冷却 + 子步） | 1 | P1 |
| P2 | KOH/NaOH 物性 + 电导率关联 + Henry 溶解 | 2 | P0 |
| P3 | BoP 对象（纯化/储氢）+ 两个流程模板 | 3–4 | P1 |
| P4 | PowerProfile + PID 增强 + 安全连锁 + Automation 时域 API | 3 | P1b |
| P5 | 黄金数据回归 + 文档 | 1–2 | P1–P4 |
| P6 | 打包、版本、清理脚本、AGENTS/PROJECTS 登记 | 1 | P5 |
| **合计** | | **15–18** | |

**最小可行交付（MVP，约 6–7 周）**：P0 + P1 + P1b + P2 的 KOH 部分 + 一个 PEM 示例流程。这已经能支撑"电解水制氢电化学 + 部分负荷 + 温度/压力/液位控制仿真"的论文级工作。

---

## 6. 风险清单

| 风险 | 等级 | 缓解 |
|---|---|---|
| 动态框架是"每 UO 自积分 + 固定步显式"，stack 热模型可能数值发散 | **高** | `RunDynamicModel` 内做子步；文档写明推荐 `IntegrationStep ≤ 5 s`；长期换集中式 DAE |
| `IsSource=True` 继承自 `CleanEnergyUnitOpBase` 会让电解槽算在最前面（顺序错误） | 高 | 新 UO 覆盖 `IsSource = False`；不要复用 `CleanEnergyUnitOpBase`，另写 `ElectrolyzerUnitOpBase` |
| ALK 的 KOH 物性在 DWSIM 里根本没有，硬做严格电解质热力学要碰 `ElectrolyteBase`/`FlashAlgorithms/ElectrolyteSVLE` | **高** | P2 只加化合物条目 + UO 内电导率关联式，**不碰 flash 算法** |
| 电解槽换热/两相分离能否收敛（PH flash + 高含气率） | 中 | 出口先 PT flash 再按 PH 修正（现有 `WaterElectrolyzer` 的做法可用）；必要时把 `AtEquilibrium=False` |
| 双 UI（WinForms + Eto）都要改编辑面板，漏一边 → macOS 上缺参数入口 | 中 | 优先只做 `PopulateEditorPanel`（Eto 路线，两平台通用，`WaterElectrolyzer` 已这样）；Classic WinForms 面板可后置 |
| 长期无法跟上游合并（目录挪动 + UO 改名） | 中 | 所有新代码放新目录 `UnitOperations/Electrolyzers/`；上游文件只做"追加式"改动（改 `.vbproj` 加编译项、加资源），不改逻辑 |
| 本机无 .NET 构建环境 | 中 | P0 必须先解决；建议 Windows VM 或 GitHub Actions `windows-latest` + msbuild |
| GPLv3 传染 | 低 | 保持 GPLv3，README 声明派生关系与改动清单 |
| 参数不可信（i0 文献差 10⁹，R4） | 中 | 参数集必须带出处与标定脚本（可用已有 `OptimizationCase`/`FormDataRegression` 回归） |

---

## 7. 明确建议"不要做"的事

1. **不要**在 `WaterElectrolyzer.vb` 里直接加极化曲线并保留 `ObjectType.WaterElectrolyzer`——会破坏已有 flowsheet 语义，且以后无法跟上游合并。
2. **不要**一上来做 SOEC / 共电解 / 高温电解。先把 PEM 与 ALK 做扎实（文献里 95% 的工作都在这两条线上）。
3. **不要**为了"统一"去重构 `DWSIM.Interfaces.Enums.ObjectType` 或改 `SimulationObjectClass` 语义（`Electrolyzers=18` 已经存在但没人用，直接复用即可）。
4. **不要**依赖 `DWSIM.ProFeatures`（闭源桥接）做经济性；LCOH 应该作为独立开源计算器（可以是 flowsheet 里的 `Spreadsheet` 或独立 Python 脚本），不引入授权依赖。
5. **不要**在公开文档/提交里写真实厂站名或厂商投标原文（工作区红线）。

---

## 8. 附：本次分析用到的关键代码位置索引

| 主题 | 位置 |
|---|---|
| 电解槽现状 | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/WaterElectrolyzer.vb` L394–545（Calculate）、L76–155（属性）、L224–310（连接器） |
| CleanEnergy 基类 | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/CleanEnergyUnitOpBase.vb` L14（`IsSource=True`）、L26（`ObjectClass`） |
| 动态契约 | `DWSIM.Interfaces/ISimulationObject.vb` L31/L45/L184；实现于 `DWSIM.SharedClasses/BaseClass/SimulationObjectBaseClasses.vb` L121（默认 False）、L357（默认抛异常） |
| 动态 UO 样板 | `DWSIM.UnitOperations/UnitOperations/Vessel.vb` L35/L135/L202；`Tank.vb` L35/L135/L202；`Reactors/CSTR.vb` L76/L156/L169 |
| 求解顺序 / Source | `DWSIM.FlowsheetSolver/FlowsheetSolver.vb` L1030、L1091–1103 |
| 动态主循环 | `DWSIM/Forms/FlowsheetComponents/FormDynamicsIntegratorControl.vb` L430–520；无 UI 版 `DWSIM.UI.Desktop.Editors/Dynamics/DynamicsIntegratorControls.cs` L441 |
| PID | `DWSIM.UnitOperations/Controllers/PIDController.vb` L70–130（参数）、L722–794（Calculate） |
| 单位体系 | `DWSIM.Interfaces/IUnitsOfMeasure.vb`、`DWSIM.Interfaces/Enums.vb` L154–221（`UnitOfMeasure`）、`DWSIM.SharedClasses/UnitsOfMeasure/SystemsOfUnits.vb`（SI/SI_ENG/English/CGS/5×Custom） |
| 参数容器范式 | `DWSIM.UnitOperations/UnitOperations/CleanEnergies/PEMFuelCellUnitOpBase.vb` L31–72（`PEMFuelCellModelParameter`、`InputParameters`/`OutputParameters`） |
| 电解质物性 | `DWSIM.Thermodynamics/PropertyPackages/ElectrolyteBase.vb` L51（`IsElectrolytePP=True`）、`ElectrolyteIdeal.vb` L38（`Ideal Solution (Aqueous Electrolytes)`）、L573（Henry） |
| 化合物库 | `DWSIM.Thermodynamics/Assets/Databases/electrolyte.xml`（84 物种）、`henry_constants.csv` |
| 外部 UO 发现 | `DWSIM.SharedClasses/Misc/Utilities.vb` L502–550（`LoadAdditionalUnitOperations`，扫 `unitops/` 与 `extenders/*Extensions*.dll`）、`DWSIM/Forms/MainWindow/FormMain.vb` L936 |
| 外部 UO 注册（画布） | `DWSIM.FlowsheetBase/FlowsheetBase.vb` L1008（`ObjectType.External`）、L1794（`WaterElectrolyzer` 特例） |
| 优化/敏感性 | `DWSIM.SharedClasses/Flowsheet/SensitivityAnalysis.vb`、`Optimization.vb`；`DWSIM.Math/IPOPTSolver.vb`、`BFGSBMinimizer.vb` |
| 自动化 API | `DWSIM.Automation/Automation.cs`（`Automation3`）；`DWSIM.Automation/Flowsheet2.cs` L23（无 UI 的 FlowsheetBase 实现） |
| 测试 | `DWSIM.Tests/Program.cs`（只跑通不校验）、`DWSIM.Automation.Tests.CSharp/*.cs` |
| 官方示例 | `PlatformFiles/Common/samples/Water Electrolyzer.dwxmz`、`Dynamic Simulation - *.dwxmz`（6 个动态示例） |
