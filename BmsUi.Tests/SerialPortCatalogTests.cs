using BmsUi.Serial;
using Xunit;

public class SerialPortCatalogTests
{
    [Theory]
    [InlineData(@"\Device\USBSER000", "USB")]       // the HV BMS board CDC port
    [InlineData(@"\Device\BthModem1", "Bluetooth")]
    [InlineData(@"\Device\VCP0", "ST-Link")]
    [InlineData(@"\Device\Silabser0", "CP210x")]
    [InlineData(@"\Device\ProlificSerial0", "Prolific")]
    [InlineData(@"\Device\Bogus0", null)]
    [InlineData("", null)]
    public void DescribeDevice_MapsKnownDrivers(string devicePath, string? expected)
        => Assert.Equal(expected, SerialPortCatalog.DescribeDevice(devicePath));

    [Fact]
    public void DescribeDevice_IsCaseInsensitive()
        => Assert.Equal("USB", SerialPortCatalog.DescribeDevice(@"\device\usbser000"));

    [Theory]
    [InlineData("COM3", 3)]
    [InlineData("COM12", 12)]
    [InlineData("COM255", 255)]
    public void PortNumber_ParsesNumericPart(string port, int expected)
        => Assert.Equal(expected, SerialPortCatalog.PortNumber(port));

    [Fact]
    public void PortNumber_SortsNumerically_NotAlphabetically()
    {
        // Text sorting would put COM12 before COM3, which looks wrong to the user
        var sorted = new[] { "COM12", "COM3", "COM9", "COM1" }
            .OrderBy(SerialPortCatalog.PortNumber)
            .ToArray();
        Assert.Equal(new[] { "COM1", "COM3", "COM9", "COM12" }, sorted);
    }

    [Fact]
    public void PortInfo_ShowsKindWhenKnown()
    {
        Assert.Equal("COM12 — USB", new PortInfo("COM12", "USB").ToString());
        Assert.Equal("COM7", new PortInfo("COM7", null).ToString());
    }

    [Fact]
    public void List_ReturnsPortsInNumericOrder_WithoutThrowing()
    {
        var ports = SerialPortCatalog.List();
        var numbers = ports.Select(p => SerialPortCatalog.PortNumber(p.Name)).ToList();
        Assert.Equal(numbers.OrderBy(n => n), numbers);
        Assert.All(ports, p => Assert.StartsWith("COM", p.Name, StringComparison.OrdinalIgnoreCase));
    }
}
