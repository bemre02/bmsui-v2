using BmsUi.Protocol;
using Xunit;

public class Crc8Tests
{
    [Fact]
    public void Compute_StandardCheckVector_Returns0xF4()
    {
        // CRC-8/SMBUS'in resmi "check" degeri: "123456789" -> 0xF4
        byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xF4, Crc8.Compute(data));
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsZero()
        => Assert.Equal(0x00, Crc8.Compute(System.Array.Empty<byte>()));

    [Fact]
    public void Compute_SingleZeroByte_ReturnsZero()
        => Assert.Equal(0x00, Crc8.Compute(new byte[] { 0x00 }));

    [Fact]
    public void Compute_SingleByte0x29_Returns0xDF()
    {
        // Firmware algoritmasi (main.cpp:2119) ile elde edilen deger
        Assert.Equal(0xDF, Crc8.Compute(new byte[] { 0x29 }));
    }
}
