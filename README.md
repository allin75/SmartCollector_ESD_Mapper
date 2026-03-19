# SmartCollector ESD Mapper

这个仓库包含两个独立的 ESD 映射项目，目标都是把 SmartCollector 或外部设备原始数据转换为本地 `Modbus TCP Server` 可读取的寄存器值。

## 项目说明

### 1. `Compal_ESD_手环`

用途：
- 读取手环系统导出的 CSV 文件
- 按线别 `A-I` 映射到本地 Modbus TCP 保持寄存器
- 支持单文件加载和目录持续监控
- 支持已处理文件归档和归档保留清理

关键入口：
- 解决方案：[Compal_ESD_手环.sln](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环.sln)
- 程序入口：[Program.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Program.cs)
- 主流程：[EsdModbusHost.cs](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Application/EsdModbusHost.cs)
- 配置文件：[appsettings.json](/E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/appsettings.json)

当前默认配置：
- 监听端口：`1502`
- 监控目录：`E:\ModbusServer\Compal_ESD_手环\test-watch`
- 归档目录：`E:\ModbusServer\Compal_ESD_手环\test-archive`

### 2. `Compal_ESD_区域静电`

用途：
- 通过串口 Modbus RTU 轮询外部设备点位
- 将采集值写入本地自实现的 Modbus TCP Server
- 对外支持功能码 `03` / `04`

关键入口：
- 解决方案：[Compal_ESD_区域静电.sln](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.sln)
- 程序入口：[Program.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/Program.cs)
- 采集与本地 Modbus Server：[DataCollectWorker.cs](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.Core/Services/DataCollectWorker.cs)
- 配置文件：[appsettings.json](/E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/appsettings.json)

当前默认配置：
- 监听端口：`502`
- 轮询周期：`1000ms`
- 已配置两个采集点：
  - `COM5 / 9600 / SlaveId 2 / SourceRegisterAddress 11 -> TargetRegisterOffset 0`
  - `COM5 / 9600 / SlaveId 2 / SourceRegisterAddress 37 -> TargetRegisterOffset 1`

## 仓库结构

```text
E:\ESDMapper
├─ Compal_ESD_手环
│  ├─ Compal_ESD_手环.sln
│  └─ Compal_ESD_手环
└─ Compal_ESD_区域静电
   ├─ Compal_ESD_区域静电.sln
   ├─ Compal_ESD_区域静电
   └─ Compal_ESD_区域静电.Core
```

## 构建

### Compal_ESD_手环

```bash
dotnet build "E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环.sln"
```

### Compal_ESD_区域静电

```bash
dotnet build "E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电.sln"
```

## 运行

### Compal_ESD_手环

直接运行：

```bash
dotnet run --project "E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Compal_ESD_手环.csproj"
```

命令行可覆盖配置，例如：

```bash
dotnet run --project "E:/ESDMapper/Compal_ESD_手环/Compal_ESD_手环/Compal_ESD_手环.csproj" -- --watch-dir "E:/ModbusServer/Compal_ESD_手环/test-watch" --port 1502
```

### Compal_ESD_区域静电

直接运行：

```bash
dotnet run --project "E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/Compal_ESD_区域静电.csproj"
```

单轮运行：

```bash
dotnet run --project "E:/ESDMapper/Compal_ESD_区域静电/Compal_ESD_区域静电/Compal_ESD_区域静电.csproj" -- --once
```

## 配置说明

### `Compal_ESD_手环/appsettings.json`

主要字段：
- `WatchDirectory`: 监控的 CSV 目录
- `Port`: 本地 Modbus TCP 端口
- `ArchiveEnabled`: 是否归档
- `ArchiveDirectory`: 归档目录
- `RetainDays`: 归档保留天数
- `MaxArchiveSizeMb`: 归档总大小限制

### `Compal_ESD_区域静电/appsettings.json`

主要字段：
- `TcpPort`: 本地 Modbus TCP 端口
- `PollingIntervalMs`: 轮询间隔
- `Mappings`: 采集映射列表

`Mappings` 示例：

```json
{
  "Id": 1,
  "Name": "COM5_Point11",
  "PortName": "COM5",
  "BaudRate": 9600,
  "ReadType": 0,
  "SlaveId": 2,
  "SourceRegisterAddress": 11,
  "TargetRegisterOffset": 0,
  "Value": 0
}
```

字段含义：
- `PortName`: 串口名，如 `COM5`
- `BaudRate`: 波特率
- `SlaveId`: Modbus RTU 站号
- `SourceRegisterAddress`: 远端设备寄存器地址
- `TargetRegisterOffset`: 本地 Modbus Server 的目标偏移
- `Value`: 最近一次值，占位字段

本地 Modbus 地址换算：
- `TargetRegisterOffset = 0` 对应 `40001`
- `TargetRegisterOffset = 1` 对应 `40002`

## 当前实现要点

### `Compal_ESD_手环`

- CSV 行分隔符为 `--`
- 只取每行最后一列作为终端状态
- 通过文件名识别线别
- 每条线固定分配 `100` 个寄存器

### `Compal_ESD_区域静电`

- 串口读取使用开源库 `NModbus.Serial`
- 本地 Modbus TCP Server 为项目内自实现，不依赖 Hsl
- 当前读取的是 Holding Register
- 本地寄存器为 0 基偏移存储

## 注意事项

- 仓库默认忽略 `bin/obj/publish/.vs/logs` 等生成产物
- 旧版发布产物保存在各项目目录的 `publish_legacy_*` 中，仅用于回溯，不是当前正式版本
- `Compal_ESD_区域静电` 现已切换到 `appsettings.json`，不要再维护旧的 `Mapper.json`

