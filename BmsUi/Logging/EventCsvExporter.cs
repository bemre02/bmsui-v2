using System.Globalization;
using System.Text;
using BmsUi.Model;

namespace BmsUi.Logging;

/// <summary>
/// Serialises the event log to CSV. Numbers and timestamps use InvariantCulture, matching
/// CsvLogger, so a Turkish locale's comma decimal separator cannot corrupt the file.
/// </summary>
public static class EventCsvExporter
{
    public const string Header = "TIMESTAMP,EVENT,STATE,DURATION_MS,SEVERITY";

    public static string ToCsv(IReadOnlyList<PackEvent> events)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(Header);
        foreach (var e in events)
        {
            string duration = e.Duration is { } d
                ? ((long)d.TotalMilliseconds).ToString(ci)
                : "";
            sb.Append('\n')
              .Append(e.At.ToString("yyyy-MM-dd HH:mm:ss.fff", ci)).Append(',')
              .Append(Escape(e.Label)).Append(',')
              .Append(State(e.Type)).Append(',')
              .Append(duration).Append(',')
              .Append(e.Severity);
        }
        return sb.ToString();
    }

    public static void Save(string path, IReadOnlyList<PackEvent> events)
        => File.WriteAllText(path, ToCsv(events));

    private static string State(PackEventType type) => type switch
    {
        PackEventType.FaultRaised => "raised",
        PackEventType.FaultCleared => "cleared",
        PackEventType.OutputOn => "on",
        _ => "off",
    };

    private static string Escape(string s) => s.Contains(',') ? $"\"{s}\"" : s;
}
