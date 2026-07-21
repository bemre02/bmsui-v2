using System.Globalization;
using System.Text;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Logging;

/// <summary>
/// Snapshot'lari CSV'ye yazar. Sayilar InvariantCulture ile yazilir — TR yerel ayarinda
/// virgul ondalik ayraci CSV'yi bozardi.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly TimeSpan _interval;
    private DateTime _lastWrite = DateTime.MinValue;

    public CsvLogger(string path, TimeSpan interval)
    {
        FilePath = path;
        _interval = interval;
        bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
        _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
        if (isNew) _writer.WriteLine(BuildHeader());
    }

    public string FilePath { get; }
    public long RowCount { get; private set; }

    public static string BuildHeader()
    {
        var sb = new StringBuilder("zaman");
        for (int i = 0; i < HvProtocol.CellCount; i++) sb.Append($",v{i}");
        for (int i = 0; i < HvProtocol.CellCount; i++) sb.Append($",t{i}");
        for (int i = 0; i < HvProtocol.SegmentCount; i++) sb.Append($",bal{i}");
        sb.Append(",pack_v,pack_a,guc_kw,soc,faults,outputs,min_v,maks_v,ort_v");
        return sb.ToString();
    }

    public void Log(BmsSnapshot s)
    {
        var now = DateTime.Now;
        if (now - _lastWrite < _interval) return;
        _lastWrite = now;

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(now.ToString("yyyy-MM-dd HH:mm:ss.fff", ci));
        foreach (double v in s.CellVoltages) sb.Append(',').Append(v.ToString("F3", ci));
        foreach (double t in s.CellTemps) sb.Append(',').Append(t.ToString("F2", ci));
        foreach (ushort b in s.BalanceBitmaps) sb.Append(',').Append(b.ToString(ci));

        var stats = s.VoltageStats;
        sb.Append(',').Append(s.PackVoltage.ToString("F2", ci))
          .Append(',').Append(s.PackCurrent.ToString("F1", ci))
          .Append(',').Append(s.PowerKw.ToString("F3", ci))
          .Append(',').Append(s.SocPercent.ToString("F2", ci))
          .Append(',').Append(s.Faults.ToString(ci))
          .Append(',').Append(s.Outputs.ToString(ci))
          .Append(',').Append(stats.Min.ToString("F3", ci))
          .Append(',').Append(stats.Max.ToString("F3", ci))
          .Append(',').Append(stats.Avg.ToString("F3", ci));

        _writer.WriteLine(sb.ToString());
        RowCount++;
    }

    public void Dispose() => _writer.Dispose();
}
