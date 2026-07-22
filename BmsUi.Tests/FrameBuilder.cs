using System.Buffers.Binary;
using BmsUi.Protocol;

/// <summary>Builds the frames the firmware would produce, for use in tests.</summary>
public static class FrameBuilder
{
    public static byte[] CellFrame(short[] rawValues, byte id)
    {
        var f = new byte[HvProtocol.CellFrameLength];
        for (int i = 0; i < HvProtocol.CellCount; i++)
            BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(i * 2, 2), rawValues[i]);
        f[192] = id;
        f[193] = Crc8.Compute(f.AsSpan(0, 193));
        return f;
    }

    public static byte[] BalanceFrame(ushort[] dcc)
    {
        var f = new byte[HvProtocol.BalanceFrameLength];
        for (int i = 0; i < HvProtocol.SegmentCount; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(i * 2, 2), dcc[i]);
        f[12] = HvProtocol.CmdBalance;
        f[13] = Crc8.Compute(f.AsSpan(0, 13));
        return f;
    }

    public static byte[] RegisterFrame(byte idx, ushort value)
    {
        var f = new byte[HvProtocol.RegisterFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0, 2), value);
        f[2] = idx;
        f[3] = Crc8.Compute(f.AsSpan(0, 3));
        return f;
    }
}
