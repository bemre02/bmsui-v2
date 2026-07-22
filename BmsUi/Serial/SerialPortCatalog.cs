using System.IO.Ports;
using Microsoft.Win32;

namespace BmsUi.Serial;

/// <summary>Bir COM portu ve ne olduguna dair kisa aciklama.</summary>
public sealed record PortInfo(string Name, string? Kind)
{
    public override string ToString() => Kind is null ? Name : $"{Name} — {Kind}";
}

/// <summary>
/// Takili COM portlarini listeler ve her birinin turunu yazar.
///
/// Tur bilgisi WMI ile de alinabilir ama Win32_PnPEntity sorgusu bu makinede ~810 ms
/// suruyor; cihaz takilip cikarildikca cagrilacagi icin cok yavas. Bunun yerine
/// HKLM\HARDWARE\DEVICEMAP\SERIALCOMM okunuyor: ayni bilgiyi tek anahtar okumasiyla
/// veriyor (\Device\USBSER000 -> COM12, \Device\BthModem1 -> COM3).
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

    /// <summary>COM10 &lt; COM9 gibi metin siralamasindan kacinmak icin sayisal sira.</summary>
    public static int PortNumber(string portName)
        => int.TryParse(portName.AsSpan().TrimStart("COMcom".ToCharArray()), out int n) ? n : int.MaxValue;

    /// <summary>Cihaz yolundan okunabilir bir tur cikarir (saf fonksiyon — test edilebilir).</summary>
    public static string? DescribeDevice(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;

        // Buyuk/kucuk harf duyarsiz karsilastirma: yollar surucuye gore degisiyor
        if (Contains(devicePath, "USBSER")) return "USB";
        if (Contains(devicePath, "BthModem") || Contains(devicePath, "BTHMODEM")) return "Bluetooth";
        if (Contains(devicePath, "VCP")) return "ST-Link";
        if (Contains(devicePath, "Silabser")) return "CP210x";
        if (Contains(devicePath, "ProlificSerial")) return "Prolific";
        if (Contains(devicePath, "FTDIBUS") || Contains(devicePath, "VCP0")) return "FTDI";
        if (Contains(devicePath, "CH34")) return "CH340";
        if (Contains(devicePath, "Serial")) return "Seri";
        return null;

        static bool Contains(string haystack, string needle)
            => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SERIALCOMM: cihaz yolu -> port adi. Okunamazsa bos sozluk doner.</summary>
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
            // Registry okunamadiysa portlari aciklamasiz gosteririz — islev kaybi yok
        }
        return result;
    }
}
