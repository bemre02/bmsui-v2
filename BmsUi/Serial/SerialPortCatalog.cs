using System.IO.Ports;
using Microsoft.Win32;

namespace BmsUi.Serial;

/// <summary>A COM port plus a short description of what it is.</summary>
public sealed record PortInfo(string Name, string? Kind)
{
    public override string ToString() => Kind is null ? Name : $"{Name} — {Kind}";
}

/// <summary>
/// Lists the attached COM ports and labels each one with its kind.
///
/// WMI could supply the kind too, but a Win32_PnPEntity query takes ~810 ms on this
/// machine and this runs on every device plug/unplug — far too slow. Instead we read
/// HKLM\HARDWARE\DEVICEMAP\SERIALCOMM, which carries the same information and costs a
/// single key read (\Device\USBSER000 -> COM12, \Device\BthModem1 -> COM3).
/// </summary>
public static class SerialPortCatalog
{
    private const string DeviceMapKey = @"HARDWARE\DEVICEMAP\SERIALCOMM";

    public static IReadOnlyList<PortInfo> List()
    {
        var kinds = ReadDeviceMap();
        return SerialPort.GetPortNames()
            .Distinct()
            .OrderBy(PortNumber)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new PortInfo(n, kinds.GetValueOrDefault(n)))
            .ToList();
    }

    /// <summary>Numeric ordering, so text sorting does not put COM10 before COM9.</summary>
    public static int PortNumber(string portName)
        => int.TryParse(portName.AsSpan().TrimStart("COMcom".ToCharArray()), out int n) ? n : int.MaxValue;

    /// <summary>Derives a readable kind from the device path (pure function — testable).</summary>
    public static string? DescribeDevice(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;

        // Case-insensitive: the exact casing of these paths varies by driver
        if (Contains(devicePath, "USBSER")) return "USB";
        if (Contains(devicePath, "BthModem") || Contains(devicePath, "BTHMODEM")) return "Bluetooth";
        if (Contains(devicePath, "VCP")) return "ST-Link";
        if (Contains(devicePath, "Silabser")) return "CP210x";
        if (Contains(devicePath, "ProlificSerial")) return "Prolific";
        if (Contains(devicePath, "FTDIBUS") || Contains(devicePath, "VCP0")) return "FTDI";
        if (Contains(devicePath, "CH34")) return "CH340";
        if (Contains(devicePath, "Serial")) return "Serial";
        return null;

        static bool Contains(string haystack, string needle)
            => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SERIALCOMM: device path -> port name. Returns an empty map if unreadable.</summary>
    private static Dictionary<string, string> ReadDeviceMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DeviceMapKey);
            if (key is null) return result;

            foreach (string devicePath in key.GetValueNames())
            {
                if (key.GetValue(devicePath) is not string port || string.IsNullOrEmpty(port))
                    continue;
                string? kind = DescribeDevice(devicePath);
                if (kind is not null) result[port] = kind;
            }
        }
        catch
        {
            // If the registry is unreadable we show ports without a kind — nothing is lost
        }
        return result;
    }
}
