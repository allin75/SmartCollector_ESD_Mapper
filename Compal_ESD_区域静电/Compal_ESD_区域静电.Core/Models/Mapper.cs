namespace Compal_ESD_区域静电.Core.Models;

public sealed class Mapper
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = 115200;

    public ReadType ReadType { get; set; } = ReadType.Serial;

    public byte SlaveId { get; set; } = 1;

    public ushort SourceRegisterAddress { get; set; }

    public int TargetRegisterOffset { get; set; }

    public short Value { get; set; }
}

public enum ReadType
{
    Serial = 0,
    Message = 1
}
