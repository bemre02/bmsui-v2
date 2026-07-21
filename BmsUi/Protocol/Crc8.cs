namespace BmsUi.Protocol;

/// <summary>
/// CRC-8/SMBUS: poly=0x07, init=0x00, refleksiyon yok, xorout=0x00.
/// Firmware karsiligi: main.cpp calculateCRC8().
/// </summary>
public static class Crc8
{
    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0x00;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int j = 0; j < 8; j++)
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }
        return crc;
    }
}
