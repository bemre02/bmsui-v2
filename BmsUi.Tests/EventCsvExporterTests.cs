using BmsUi.Logging;
using BmsUi.Model;
using Xunit;

public class EventCsvExporterTests
{
    private static readonly DateTime T0 = new(2026, 7, 23, 14, 0, 0);

    [Fact]
    public void ToCsv_WritesHeaderThenOneLinePerEvent()
    {
        var events = new List<PackEvent>
        {
            new(T0, PackEventType.FaultRaised, "Cell overvoltage", null, EventSeverity.Critical),
            // Exact-millisecond timestamp: AddSeconds(2.3) formats as .299/.300 depending on
            // the BCL, so pin it to 2300 ms exactly (same reasoning as the Task 1 duration test).
            new(T0.AddMilliseconds(2300), PackEventType.FaultCleared, "Cell overvoltage",
                TimeSpan.FromMilliseconds(2300), EventSeverity.Info),
        };

        var lines = EventCsvExporter.ToCsv(events).Split('\n');

        Assert.Equal("TIMESTAMP,EVENT,STATE,DURATION_MS,SEVERITY", lines[0]);
        Assert.Equal("2026-07-23 14:00:00.000,Cell overvoltage,raised,,Critical", lines[1]);
        Assert.Equal("2026-07-23 14:00:02.300,Cell overvoltage,cleared,2300,Info", lines[2]);
    }

    [Fact]
    public void ToCsv_QuotesALabelThatContainsAComma()
    {
        var events = new List<PackEvent>
        {
            new(T0, PackEventType.OutputOn, "A, B", null, EventSeverity.Info),
        };
        Assert.Contains("\"A, B\"", EventCsvExporter.ToCsv(events));
    }
}
