using BmsUi.Protocol;
using Xunit;

/// <summary>
/// bms_simulator.py'nin URETTIGI gercek baytlar. Python tarafi ile C# ayristiricisinin
/// (CRC, endianlik, isaretlilik) birebir uyustugunu kanitlar — protokolde iki dilin
/// ayrisması en sinsi hata kaynagi oldugu icin cerceveler sabit olarak saklanir.
/// </summary>
public class SimulatorFrameTests
{
    private static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    // volts[i] = 3.30 + i*0.009  -> raw 330..415
    private const string VoltageFrameHex =
        "4a014a014b014c014d014e014f01500151015201520153015401550156015701580159015a015b01" +
        "5c015c015d015e015f01600161016201630164016501650166016701680169016a016b016c016d01" +
        "6d016e016f01700171017201730174017501760177017701780179017a017b017c017d017e017f01" +
        "8001800181018201830184018501860187018801890189018a018b018c018d018e018f0190019101" +
        "9101920193019401950196019701980199019a019a019b019c019d019e019f01290f";

    // temps[i] = -12.5 + i*0.75 -> raw -1250..5875 (ISARETLI)
    private const string TempFrameHex =
        "1efb69fbb4fbfffb4afc95fce0fc2bfd76fdc1fd0cfe57fea2feedfe38ff83ffceff19006400af00" +
        "fa0045019001db0126027102bc02070352039d03e80333047e04c90414055f05aa05f50540068b06" +
        "d60621076c07b70702084d089808e3082e097909c4090f0a5a0aa50af00a3b0b860bd10b1c0c670c" +
        "b20cfd0c480d930dde0d290e740ebf0e0a0f550fa00feb0f36108110cc1017116211ad11f8114312" +
        "8e12d91224136f13ba13051450149b14e61431157c15c71512165d16a816f3162a03";

    private const string BalanceFrameHex = "0500000000000000000000802bad";
    private const string RegisterFrameHex = "89fe08cb";   // idx 8 (PACK_CURRENT) = -375

    [Fact]
    public void SimulatorVoltageFrame_ParsesToExpectedRange()
    {
        var volts = new double[HvProtocol.CellCount];
        Assert.True(FrameParser.TryParseCellVoltages(FromHex(VoltageFrameHex), volts, out var err), err);
        Assert.Equal(3.30, volts[0], 3);
        Assert.Equal(4.15, volts[95], 3);
    }

    [Fact]
    public void SimulatorTempFrame_ParsesNegativeAndPositive()
    {
        var temps = new double[HvProtocol.CellCount];
        Assert.True(FrameParser.TryParseCellTemps(FromHex(TempFrameHex), temps, out var err), err);
        Assert.Equal(-12.50, temps[0], 3);     // negatif ucu — isaretli okuma kaniti
        Assert.Equal(58.75, temps[95], 3);
    }

    [Fact]
    public void SimulatorBalanceFrame_ParsesBitmaps()
    {
        var maps = new ushort[HvProtocol.SegmentCount];
        Assert.True(FrameParser.TryParseBalance(FromHex(BalanceFrameHex), maps, out var err), err);
        Assert.Equal(0x0005, maps[0]);        // segment 0: hucre 0 ve 2
        Assert.Equal(0x8000, maps[5]);        // segment 5: hucre 15
    }

    [Fact]
    public void SimulatorRegisterFrame_ParsesNegativeCurrent()
    {
        Assert.True(FrameParser.TryParseRegister(FromHex(RegisterFrameHex), Reg.PackCurrent,
                                                 out var raw, out var err), err);
        Assert.Equal(-37.5, (short)raw / 10.0, 3);
    }
}
