# AGENTS

本文件面向后续在本仓库内工作的开发代理或维护者，说明当前代码结构、关键约束和推荐修改入口。

## 仓库目标

仓库维护两个独立的 ESD Mapper：

1. `Compal_ESD_手环`
   - 数据源：CSV 文件
   - 输出：本地 Modbus TCP Server
   - 特点：按线别分区、支持目录监控与归档

2. `Compal_ESD_区域静电`
   - 数据源：串口 Modbus RTU
   - 输出：本地 Modbus TCP Server
   - 特点：使用 `NModbus.Serial` 采集，配置入口为 `appsettings.json`

## 关键事实

### `Compal_ESD_手环`

- 入口在 [Program.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Program.cs)
- 主流程在 [EsdModbusHost.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Application/EsdModbusHost.cs)
- 本地 Modbus Server 在 [ModbusTcpServer.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Infrastructure/ModbusTcpServer.cs)
- 寄存器布局在 [RegisterLayout.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Domain/RegisterLayout.cs)

约束：
- 一条线固定 `100` 个寄存器
- 支持线别 `A-I`
- CSV 解析只取每行最后一列状态值

### `Compal_ESD_区域静电`

- 入口在 [Program.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/Program.cs)
- 运行时配置在 [appsettings.json](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/appsettings.json)
- 配置加载在 [CollectorRuntimeOptions.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Services/CollectorRuntimeOptions.cs)
- 串口采集和本地 Modbus Server 在 [DataCollectWorker.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Services/DataCollectWorker.cs)

约束：
- 不再使用 `HslCommunication`
- 串口读取使用 `NModbus.Serial`
- 本地寄存器偏移是 0 基
- `TargetRegisterOffset = 0` 对应 Modbus `40001`
- 当前已修复“多个点位都落到偏移 0”的问题，不要重新引入任何二次归一化逻辑

## 修改建议

### 修改 `Compal_ESD_手环`

优先查看这些文件：
- [appsettings.json](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/appsettings.json)
- [StartupOptions.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Configuration/StartupOptions.cs)
- [CsvDirectoryMonitor.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Services/CsvDirectoryMonitor.cs)
- [CsvStatusLoader.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Services/CsvStatusLoader.cs)

如果改寄存器布局：
- 同时检查 [RegisterBank.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Domain/RegisterBank.cs)
- 同时检查启动日志里的地址说明输出

### 修改 `Compal_ESD_区域静电`

优先查看这些文件：
- [appsettings.json](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/appsettings.json)
- [Mapper.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Models/Mapper.cs)
- [CollectorRuntimeOptions.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Services/CollectorRuntimeOptions.cs)
- [DataCollectWorker.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Services/DataCollectWorker.cs)

如果改采集行为：
- 明确是改 `SlaveId`、`SourceRegisterAddress`，还是改本地 `TargetRegisterOffset`
- 不要把 `TargetRegisterOffset` 当成 `40001` 风格地址再次换算

如果改本地 Modbus Server：
- `03` / `04` 功能码支持在 `DataCollectWorker.cs` 内部的 `ModbusTcpServer`
- 改异常码或地址范围时，要同步做 TCP 读回验证

## 推荐验证

### 构建

```bash
dotnet build "E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环.sln"
dotnet build "E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.sln"
```

### `Compal_ESD_区域静电`

最低验证：
1. 启动程序
2. 看日志是否输出每个 `targetRegisterOffset`
3. 用 Modbus TCP 读回 `40001/40002/...`

### `Compal_ESD_手环`

最低验证：
1. 放入符合命名规则的 CSV
2. 看日志是否识别到线别
3. 从对应线别寄存器区读回值

## 不要做的事

- 不要重新启用 `HslCommunication`
- 不要恢复 `Compal_ESD_区域静电` 的 `Mapper.json` 作为主配置入口
- 不要把 `publish_legacy_*` 当成当前正式版本继续修改
- 不要把 `bin/obj/publish/.vs/logs` 等生成目录纳入版本控制

