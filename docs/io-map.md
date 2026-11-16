# H2sim Modbus I/O 映射

H2sim 作为 **Modbus TCP 从站（server）**，OpenPLC v4 用 `modbus_master` 轮询。
物理世界里 PLC 是主站，所以把模型换成真实设备时 PLC 程序不用改。

## 区域与方向

| Modbus 区域 | 功能码 | OpenPLC 记法 | 方向 | 用途 |
|---|---|---|---|---|
| Coil | 01 / 05 / 15 | `%QX` | `FromPlc`（PLC 写） | 布尔命令 |
| Discrete Input | 02 | `%IX` | `ToPlc`（PLC 读） | 布尔状态 |
| Holding Register | 03 / 06 / 16 | `%QW` | `FromPlc`（PLC 写） | 设定值 / 模拟量命令 |
| Input Register | 04 | `%IW` | `ToPlc`（PLC 读） | 测量值 |

- `Signal.ModbusAddress` 在代码里是 **0-based**；写进 flowsheet XML 时是 **1-based**（根节点 `ModbusAddressBase="1"`）。
- 一个 `Int32` / `Float32` 占 **2 个连续寄存器**；`Int16` / `Bool` 占 1 个。
- 同一区域内的地址应连续，OpenPLC 主站按功能码分组读取；不连续会被拆成多个请求，拉长扫描周期。

## 数据类型与缩放

原始值 = 工程值 × `Scale` + `Offset`；工程值 = (原始值 − `Offset`) / `Scale`。

- OpenPLC v4 的 `modbus_slave` 分段没有 REAL 段，**模拟量优先用 `Int16`**，例如温度 85.3 °C 用 `Scale=10` 存成 853。
- `Int32` / `Float32` 支持 `WordOrder`：`ABCD`（默认，高字在前）、`CDAB`、`BADC`、`DCBA`。
- `Bool` 不参与缩放。

## 质量码约定

Modbus 没有质量码，采用以下约定：

- 模型计算失败或桥接降级时，所有 `ToPlc` 模拟量写 **`0x8000`（-32768）** 哨兵值。
- `Coil 0` 是 `ALM.ModelFault`：正常为 `false`，模型故障时为 `true`。
- 恢复 `Good` 后，下一次 `PublishOutputs()` 会覆盖哨兵值并复位故障线圈。

## 地址规划（示例）

| 点 | 区域 | 地址（0-based） | 类型 | Scale | 单位 | 方向 |
|---|---|---|---|---|---|---|
| `WE1.P_set` | Holding Register | 0 | Int16 | 10 | kW | FromPlc |
| `WE1.T_set` | Holding Register | 1 | Int16 | 10 | degC | FromPlc |
| `WE1.P_cat_set` | Holding Register | 2 | Int16 | 100 | bar | FromPlc |
| `WE1.Start` | Coil | 0 | Bool | — | — | FromPlc |
| `WE1.T_stack` | Input Register | 0 | Int16 | 100 | degC | ToPlc |
| `WE1.P_stack` | Input Register | 1 | Int16 | 10 | kW | ToPlc |
| `WE1.H2inO2` | Input Register | 2 | Int16 | 100 | vol% | ToPlc |
| `WE1.Running` | Discrete Input | 0 | Bool | — | — | ToPlc |
| `ALM.ModelFault` | Coil | 0 | Bool | — | — | ToPlc（保留） |

> 实际项目按设备点表扩展；不要复用已占用的地址（校验器会报 `address overlap`）。
>
> 温度采用 `Scale=100`（0.01 °C 分辨率），避免 0.1 °C 量化死区给闭环带来
> 约 0.4 K 稳态误差；OpenPLC 三回路模板的模拟执行器也采用 0.01% 分辨率。

## 连接配置

```xml
<Connection Name="OpenPLC"
            Protocol="ModbusTcpServer"
            Host="0.0.0.0"
            Port="502"
            UnitId="1"
            Enabled="true" />
```

`SignalMap` 存在 flowsheet 的 `ExtraProperties["H2sim.SignalMap"]` 下，随 flowsheet 保存/加载。

## 线程模型

- Modbus 线程只读写 `ModbusDataSnapshot`（加锁的寄存器/线圈镜像），不碰模型对象。
- 模型线程每步开头 `ApplyInputs()` 读快照并调用 `ControlArbiter.WriteExternal()`；步骤结束 `PublishOutputs()` 写快照。
- **绝不在 Modbus 线程里调用 `ISimulationObject.SetPropertyValue`。**
