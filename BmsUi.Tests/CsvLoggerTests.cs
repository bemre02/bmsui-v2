using BmsUi.Logging;
using BmsUi.Model;
using BmsUi.Protocol;
using Xunit;

public class CsvLoggerTests
{
    private static string TempCsv()
        => Path.Combine(Path.GetTempPath(), $"bmsui_test_{Guid.NewGuid():N}.csv");

    [Fact]
    public void BuildHeader_HasAllColumns()
    {
        var cols = CsvLogger.BuildHeader().Split(',');
        // TIMESTAMP + 96 voltaj + 96 sicaklik + 6 balans + 17 ozet alani
        Assert.Equal(1 + 96 + 96 + 6 + 17, cols.Length);
        Assert.Equal("TIMESTAMP", cols[0]);
        // Takimin SD kart sablonuyla ayni isimlendirme
        Assert.Equal("BMS_CELL0_VOLTAGE_f", cols[1]);
        Assert.Equal("BMS_CELL95_VOLTAGE_f", cols[96]);
        Assert.Equal("BMS_CELL0_TEMP_f", cols[97]);
        Assert.Equal("BMS_CELL95_TEMP_f", cols[192]);
        Assert.Equal("BMS_BALANCE_IC0_u16", cols[193]);
        Assert.Contains("BMS_CELL_VOLTAGE_STDDEV_f", cols);
        Assert.Contains("BMS_CONTRACTORS_u8", cols);
        Assert.Contains("BMS_ESTIMATED_SoC_f", cols);
    }

    [Fact]
    public void Log_RespectsInterval()
    {
        string path = TempCsv();
        try
        {
            using (var logger = new CsvLogger(path, TimeSpan.FromMinutes(1)))
            {
                var s = BmsSnapshot.Empty();
                logger.Log(s);
                logger.Log(s);      // araligi doldurmadi -> yazilmamali
                Assert.Equal(1, logger.RowCount);
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);          // baslik + 1 satir
            Assert.StartsWith("TIMESTAMP,", lines[0]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Log_WritesInvariantDecimalPoint()
    {
        string path = TempCsv();
        try
        {
            var s = BmsSnapshot.Empty();
            var volts = new double[HvProtocol.CellCount];
            volts[0] = 3.875;
            s.SetVoltages(volts);

            using (var logger = new CsvLogger(path, TimeSpan.Zero)) logger.Log(s);

            string row = File.ReadAllLines(path)[1];
            Assert.Contains("3.875", row);       // TR yerel ayarinda bile nokta kullanilmali
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Log_RowHasSameColumnCountAsHeader()
    {
        string path = TempCsv();
        try
        {
            using (var logger = new CsvLogger(path, TimeSpan.Zero)) logger.Log(BmsSnapshot.Empty());
            var lines = File.ReadAllLines(path);
            Assert.Equal(lines[0].Split(',').Length, lines[1].Split(',').Length);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Constructor_AppendsWithoutDuplicatingHeader()
    {
        string path = TempCsv();
        try
        {
            using (var l1 = new CsvLogger(path, TimeSpan.Zero)) l1.Log(BmsSnapshot.Empty());
            using (var l2 = new CsvLogger(path, TimeSpan.Zero)) l2.Log(BmsSnapshot.Empty());

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);              // 1 baslik + 2 satir
            Assert.Single(lines, l => l.StartsWith("TIMESTAMP,"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
