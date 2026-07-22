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

    /// <summary>
    /// Sütun adları takımın SD kart şablonuyla aynı: ALTSISTEM_SINYAL_tip
    /// (`BMS_CELL42_VOLTAGE_f`, `BMS_MAX_CELL_TEMP_f`, `BMS_CONTRACTORS_u8` ...).
    /// Böylece SD kart logları ile bu CSV aynı araçlarla işlenebiliyor.
    ///
    /// Birimler: voltaj V, sıcaklık °C, akım A, güç W, SoC %, FAULTS/CONTRACTORS bit maskesi,
    /// balans bitmap'i IC başına 16 bit.
    /// </summary>
    public static string BuildHeader()
    {
        var sb = new StringBuilder("TIMESTAMP");
        for (int i = 0; i < HvProtocol.CellCount; i++) sb.Append($",BMS_CELL{i}_VOLTAGE_f");
        for (int i = 0; i < HvProtocol.CellCount; i++) sb.Append($",BMS_CELL{i}_TEMP_f");
        for (int i = 0; i < HvProtocol.SegmentCount; i++) sb.Append($",BMS_BALANCE_IC{i}_u16");
        sb.Append(",BMS_TOTAL_VOLTAGE_f")
          .Append(",BMS_TOTAL_CELL_VOLTAGE_f")
          .Append(",BMS_CURRENT_f")
          .Append(",BMS_POWER_f")
          .Append(",BMS_ESTIMATED_SoC_f")
          .Append(",BMS_FAULTS_u16")
          .Append(",BMS_CONTRACTORS_u8")
          .Append(",BMS_MIN_CELL_VOLTAGE_f")
          .Append(",BMS_MAX_CELL_VOLTAGE_f")
          .Append(",BMS_AVG_CELL_VOLTAGE_f")
          .Append(",BMS_CELL_VOLTAGE_STDDEV_f")
          .Append(",BMS_MIN_CELL_NUMBER_u8")
          .Append(",BMS_MAX_CELL_NUMBER_u8")
          .Append(",BMS_MIN_CELL_TEMP_f")
          .Append(",BMS_MAX_CELL_TEMP_f")
          .Append(",BMS_AVG_CELL_TEMP_f")
          .Append(",BMS_MAX_SLAVE_TEMP_f");
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

        var volt = s.VoltageStats;
        var temp = s.TempStats;
        var analysis = s.VoltageAnalysis;

        sb.Append(',').Append(s.PackVoltage.ToString("F2", ci))
          .Append(',').Append(s.TotalCellVoltage.ToString("F2", ci))
          .Append(',').Append(s.PackCurrent.ToString("F1", ci))
          .Append(',').Append((s.PowerKw * 1000.0).ToString("F1", ci))
          .Append(',').Append(s.SocPercent.ToString("F2", ci))
          .Append(',').Append(s.Faults.ToString(ci))
          .Append(',').Append(s.Outputs.ToString(ci))
          .Append(',').Append(volt.Min.ToString("F3", ci))
          .Append(',').Append(volt.Max.ToString("F3", ci))
          .Append(',').Append(volt.Avg.ToString("F3", ci))
          .Append(',').Append(analysis.StdDev.ToString("F5", ci))
          .Append(',').Append(volt.MinIndex.ToString(ci))
          .Append(',').Append(volt.MaxIndex.ToString(ci))
          .Append(',').Append(temp.Min.ToString("F2", ci))
          .Append(',').Append(temp.Max.ToString("F2", ci))
          .Append(',').Append(temp.Avg.ToString("F2", ci))
          .Append(',').Append(s.MaxSlaveTemp.ToString("F2", ci));

        _writer.WriteLine(sb.ToString());
        RowCount++;
    }

    public void Dispose() => _writer.Dispose();
}
