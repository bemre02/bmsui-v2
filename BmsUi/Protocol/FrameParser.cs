using System.Buffers.Binary;

namespace BmsUi.Protocol;

/// <summary>Validates and decodes the fixed-length binary frames sent by the device.</summary>
public static class FrameParser
{
    private static bool ValidateEnvelope(ReadOnlySpan<byte> frame, int expectedLength,
                                         byte expectedId, out string? error)
    {
        if (frame.Length != expectedLength)
        {
            error = $"Expected {expectedLength} bytes, got {frame.Length}";
            return false;
        }
        if (frame[expectedLength - 2] != expectedId)
        {
            error = $"Frame id mismatch: expected 0x{expectedId:X2}, " +
                    $"got 0x{frame[expectedLength - 2]:X2}";
            return false;
        }
        byte crc = Crc8.Compute(frame[..(expectedLength - 1)]);
        if (crc != frame[expectedLength - 1])
        {
            error = $"CRC mismatch: computed 0x{crc:X2}, " +
                    $"got 0x{frame[expectedLength - 1]:X2}";
            return false;
        }
        error = null;
        return true;
    }

    public static bool TryParseCellVoltages(ReadOnlySpan<byte> frame, double[] target,
                                            out string? error)
    {
        if (!ValidateEnvelope(frame, HvProtocol.CellFrameLength,
                              HvProtocol.CmdCellVoltages, out error)) return false;
        for (int i = 0; i < HvProtocol.CellCount; i++)
            target[i] = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(i * 2, 2)) / 100.0;
        return true;
    }

    public static bool TryParseCellTemps(ReadOnlySpan<byte> frame, double[] target,
                                         out string? error)
    {
        if (!ValidateEnvelope(frame, HvProtocol.CellFrameLength,
                              HvProtocol.CmdCellTemps, out error)) return false;
        // SIGNED: must be read as int16, otherwise negative temperatures show up as 655.xx
        for (int i = 0; i < HvProtocol.CellCount; i++)
            target[i] = BinaryPrimitives.ReadInt16LittleEndian(frame.Slice(i * 2, 2)) / 100.0;
        return true;
    }

    public static bool TryParseBalance(ReadOnlySpan<byte> frame, ushort[] target,
                                       out string? error)
    {
        if (!ValidateEnvelope(frame, HvProtocol.BalanceFrameLength,
                              HvProtocol.CmdBalance, out error)) return false;
        for (int i = 0; i < HvProtocol.SegmentCount; i++)
            target[i] = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(i * 2, 2));
        return true;
    }

    public static bool TryParseRegister(ReadOnlySpan<byte> frame, byte expectedIndex,
                                        out ushort value, out string? error)
    {
        value = 0;
        if (!ValidateEnvelope(frame, HvProtocol.RegisterFrameLength,
                              expectedIndex, out error)) return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]);
        return true;
    }
}
