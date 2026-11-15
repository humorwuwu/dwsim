# 电解槽参数

参数由 `ElectrolyzerModelParameter` 保存到 flowsheet，默认值在
`PEMElectrolyzer.SetupParameters()` / `AlkalineElectrolyzer.SetupParameters()`。

## PEM（默认值来自 Bzovsky 2024, DOI 10.3311/PPch.38170）

| 参数 | 默认 | 单位 | 说明 |
|---|---|---|---|
| `A_cell` | 1000 | cm2 | 单电池活性面积 |
| `N_cells` | 182 | — | 电池片数 |
| `i_max` | 2.5 | A/cm2 | 最大电流密度 |
| `V_cell_max` | 2.2 | V | 最大单电池电压 |
| `i_min_frac` | 0.10 | — | 最低负荷比例 |
| `i0_an_ref` | 2.16e6 | A/cm2 | 阳极交换电流密度（参考温度） |
| `E_act_an` | 76000 | J/mol | 阳极活化能 |
| `alpha_an` | 0.5 | — | 阳极传递系数 |
| `i_L` | 6.0 | A/cm2 | 极限电流密度 |
| `t_mem` | 0.0178 | cm | 膜厚 |
| `lambda_mem` | 22 | — | 膜水含量 |
| `rho_electrode` | 7.5 | mOhm*cm | 电极电阻率 |
| `t_electrode` | 0.1 | cm | 电极厚度 |
| `P_cat` / `P_an` | 30 / 2 | bar | 阴极 / 阳极压力 |
| `T_op` | 353.15 | K | 运行温度 |
| `etaF_ref` | 0.99 | — | 参考 Faraday 效率 |
| `C_stack` | 5e5 | J/K | 堆热容（动态） |
| `U_loss_area` | 50 | W/K | 堆散热系数 |

## Alkaline（R1/Dinh 2021 量级；部分系数为占位）

| 参数 | 默认 | 单位 | 说明 |
|---|---|---|---|
| `N_cells` | 300 | — | 3 MW 级 |
| `P_cat` / `P_an` | 16 / 16 | bar | 运行压力 |
| `T_op` | 353.15 | K | 运行温度 |
| `etaF_ref` | 0.86 | — | 工业 ALK 额定点 |
| `KOH_wt` | 0.30 | — | KOH 质量分数 |
| `i_max` | 0.4 | A/cm2 | 最大电流密度 |

`ModelAlkaline_SemiEmpirical` 的 `a_act`、`b_act`、`R_ohm_ref`、`E_act_ohm`
和 Faraday 低载曲线、`crossover_coeff` 仍是占位值，等 R9（电导率关联式）和 R2
（气体穿透）数据标定。极化曲线/SEC 对 Bzovsky 2024 的 <2%/<3% 对比也待数据。

## 联锁阈值

| 参数 | PEM 默认 | 单位 |
|---|---|---|
| `H2inO2_alarm` / `H2inO2_trip` | 2 / 4 | vol% |
| `O2inH2_alarm` | 2 | vol% |
| `StackTemperature_trip` | 363.15（PEM）/ 368.15（ALK） | K |
| `dP_max` | 30（PEM）/ 16（ALK） | bar |
| `WaterLevel_trip` | 5 | % |