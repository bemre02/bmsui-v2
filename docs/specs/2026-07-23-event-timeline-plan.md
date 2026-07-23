# Fault & Event Timeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A host-side latch over FAULTS and OUTPUTS that records every bit and contactor transition with a timestamp, shown in a new owner-drawn Events tab with manual CSV export.

**Architecture:** A pure `EventLog` state machine diffs consecutive FAULTS/OUTPUTS masks and emits `PackEvent` values into an in-memory ring buffer (the timestamp is injected, so durations are deterministic under test). `Form1.ApplySnapshot` feeds it every snapshot; `Disconnect` re-baselines it. A new `EventTimeline` control renders the buffer newest-first, drawn exactly like the existing `RegisterTable`. `EventCsvExporter` serialises the buffer on demand.

**Tech Stack:** C# .NET 10 WinForms (`net10.0-windows`), xUnit, owner-drawn `ListView`.

## Global Constraints

- Target framework `net10.0-windows`; the app and tests are WinForms (`UseWindowsForms`).
- The UI is **read-only** — it never writes to the BMS. This feature adds no transmit path.
- All file and numeric formatting uses `CultureInfo.InvariantCulture`; a Turkish locale's comma decimal separator must not corrupt any file (matches `CsvLogger`).
- UI controls are owner-drawn in the dark theme; `SystemColors.Control` must appear nowhere in a rendered tab (an existing test, `EveryTab_PaintsWithNoSystemChromeLeft`, enforces this across all tabs).
- WinForms tests run on an STA thread via the `RunSta` helper already in `BmsUi.Tests/UiSmokeTests.cs`.
- Any test that asserts on rendered pixels must be run once against the unfixed code to confirm it actually fails (recorded in `docs/design-decisions.md`).
- `dotnet test` must end `Başarısız: 0`; `dotnet build -c Release` must end `0 Uyarı, 0 Hata`. The Release build fails if the app is running (locked exe) — close it first.
- Commit messages end with the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work in `C:\Users\burem\STM32CubeIDE\workspace_1.19.0\bmsui-v2`. Build/test command prefix: `export PATH="/c/Program Files/dotnet:$PATH"`.

## File Structure

- Create `BmsUi/Model/PackEvent.cs` — the event value type and its two enums. Pure data.
- Create `BmsUi/Model/EventLog.cs` — the pure diff/latch state machine and ring buffer.
- Create `BmsUi/Logging/EventCsvExporter.cs` — serialise events to CSV text / file.
- Create `BmsUi/Ui/EventTimeline.cs` — owner-drawn, newest-first list control.
- Modify `BmsUi/Form1.Designer.cs` — the Events tab, the control, Clear/Export buttons, the count label, and the field declarations.
- Modify `BmsUi/Form1.cs` — construct and feed the `EventLog`, wire the buttons, re-baseline on disconnect, expose test accessors.
- Modify `docs/design-decisions.md` and `README.md` — record the feature.
- Tests: `BmsUi.Tests/EventLogTests.cs`, `BmsUi.Tests/EventCsvExporterTests.cs`, and one added test in `BmsUi.Tests/UiSmokeTests.cs`.

---

### Task 1: PackEvent value type and the EventLog core

**Files:**
- Create: `BmsUi/Model/PackEvent.cs`
- Create: `BmsUi/Model/EventLog.cs`
- Test: `BmsUi.Tests/EventLogTests.cs`

**Interfaces:**
- Consumes: `BmsUi.Protocol.FaultBits.Names` (15 fault-bit names), `BmsUi.Protocol.OutputBits` (`Air`, `Pre`, `Err`).
- Produces:
  - `enum PackEventType { FaultRaised, FaultCleared, OutputOn, OutputOff }`
  - `enum EventSeverity { Info, Critical }`
  - `readonly record struct PackEvent(DateTime At, PackEventType Type, string Label, TimeSpan? Duration, EventSeverity Severity)`
  - `EventLog` with `IReadOnlyList<PackEvent> Events { get; }`, `int DroppedCount { get; }`, `IReadOnlyList<PackEvent> Observe(ushort faults, ushort outputs, DateTime at)`, `void SetDisconnected()`, `void Clear()`.

- [ ] **Step 1: Write the failing tests**

Create `BmsUi.Tests/EventLogTests.cs`:

```csharp
using BmsUi.Model;
using BmsUi.Protocol;
using Xunit;

public class EventLogTests
{
    private static readonly DateTime T0 = new(2026, 7, 23, 14, 0, 0);
    private const ushort Overvoltage = 1 << 2;   // "Cell overvoltage"
    private const ushort Overtemp    = 1 << 6;   // "Cell overtemperature"

    [Fact]
    public void FirstSample_IsASilentBaseline()
    {
        var log = new EventLog();
        var emitted = log.Observe(Overvoltage, OutputBits.Air, T0);
        Assert.Empty(emitted);
        Assert.Empty(log.Events);
    }

    [Fact]
    public void SteadyState_EmitsNothing()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(0, 0, T0.AddMilliseconds(100));
        Assert.Empty(emitted);
    }

    [Fact]
    public void RisingFaultBit_EmitsOneRaised()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(Overvoltage, 0, T0.AddMilliseconds(100));

        var e = Assert.Single(emitted);
        Assert.Equal(PackEventType.FaultRaised, e.Type);
        Assert.Equal("Cell overvoltage", e.Label);
        Assert.Equal(EventSeverity.Critical, e.Severity);
        Assert.Null(e.Duration);
    }

    [Fact]
    public void FallingFaultBit_EmitsClearedWithDuration()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, 0, T0.AddMilliseconds(1000));
        var emitted = log.Observe(0, 0, T0.AddMilliseconds(3300));

        var e = Assert.Single(emitted);
        Assert.Equal(PackEventType.FaultCleared, e.Type);
        Assert.Equal("Cell overvoltage", e.Label);
        // Exact-millisecond timestamps: the subtraction is exact, so no float tolerance is
        // needed. TimeSpan.FromSeconds(2.3) would differ by a tick against DateTime maths.
        Assert.Equal(TimeSpan.FromMilliseconds(2300), e.Duration);
        Assert.Equal(EventSeverity.Info, e.Severity);
    }

    [Fact]
    public void TwoBitsChangingInOneSample_EmitTwoEvents()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        var emitted = log.Observe(Overvoltage | Overtemp, 0, T0.AddMilliseconds(100));

        Assert.Equal(2, emitted.Count);
        Assert.Contains(emitted, x => x.Label == "Cell overvoltage");
        Assert.Contains(emitted, x => x.Label == "Cell overtemperature");
    }

    [Fact]
    public void OutputTransitions_CarryTheRightSeverity()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);

        var air = Assert.Single(log.Observe(0, OutputBits.Air, T0.AddMilliseconds(100)));
        Assert.Equal(PackEventType.OutputOn, air.Type);
        Assert.Equal("AIR closed", air.Label);
        Assert.Equal(EventSeverity.Info, air.Severity);

        var err = Assert.Single(log.Observe(0, OutputBits.Air | OutputBits.Err, T0.AddMilliseconds(200)));
        Assert.Equal("ERR raised", err.Label);
        Assert.Equal(EventSeverity.Critical, err.Severity);

        var errOff = Assert.Single(log.Observe(0, OutputBits.Air, T0.AddMilliseconds(300)));
        Assert.Equal(PackEventType.OutputOff, errOff.Type);
        Assert.Equal("ERR cleared", errOff.Label);
    }

    [Fact]
    public void ReconnectReBaselinesSilently()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.SetDisconnected();

        // A fault is present on the first sample after reconnect: no event, just a new baseline
        var afterGap = log.Observe(Overvoltage, 0, T0.AddSeconds(10));
        Assert.Empty(afterGap);

        // ...and a change from that baseline emits again
        var cleared = log.Observe(0, 0, T0.AddSeconds(11));
        Assert.Single(cleared);
    }

    [Fact]
    public void Disconnect_DoesNotEmitClears()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, OutputBits.Air, T0.AddSeconds(1));
        int before = log.Events.Count;

        log.SetDisconnected();
        Assert.Equal(before, log.Events.Count);   // the disconnect itself added nothing
    }

    [Fact]
    public void RingBuffer_DropsOldestPastCapacity()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);

        // Toggle one bit on and off 1200 times -> 2400 events, capacity is 1000
        for (int i = 0; i < 1200; i++)
        {
            log.Observe(Overvoltage, 0, T0.AddMilliseconds(i * 2 + 1));
            log.Observe(0, 0, T0.AddMilliseconds(i * 2 + 2));
        }

        Assert.Equal(1000, log.Events.Count);
        Assert.Equal(1400, log.DroppedCount);
    }

    [Fact]
    public void Clear_EmptiesBufferButKeepsBaseline()
    {
        var log = new EventLog();
        log.Observe(0, 0, T0);
        log.Observe(Overvoltage, 0, T0.AddSeconds(1));
        Assert.NotEmpty(log.Events);

        log.Clear();
        Assert.Empty(log.Events);
        Assert.Equal(0, log.DroppedCount);

        // Baseline kept: an unchanged sample still emits nothing, no phantom "raised"
        Assert.Empty(log.Observe(Overvoltage, 0, T0.AddSeconds(2)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventLogTests"`
Expected: FAIL — `PackEvent` / `EventLog` do not exist (compile error).

- [ ] **Step 3: Create the value type**

Create `BmsUi/Model/PackEvent.cs`:

```csharp
namespace BmsUi.Model;

public enum PackEventType { FaultRaised, FaultCleared, OutputOn, OutputOff }

public enum EventSeverity { Info, Critical }

/// <summary>
/// One recorded pack event. <see cref="Label"/> is built when the event is emitted so the UI
/// and the exporter render identical text with no shared formatting. <see cref="Duration"/> is
/// set only on <see cref="PackEventType.FaultCleared"/>.
/// </summary>
public readonly record struct PackEvent(
    DateTime At, PackEventType Type, string Label, TimeSpan? Duration, EventSeverity Severity);
```

- [ ] **Step 4: Create the EventLog**

Create `BmsUi/Model/EventLog.cs`:

```csharp
using BmsUi.Protocol;

namespace BmsUi.Model;

/// <summary>
/// A pure state machine over the FAULTS and OUTPUTS registers. It diffs each sample against the
/// previous one and emits a <see cref="PackEvent"/> per changed bit into an in-memory ring
/// buffer. It holds no timer and never reads the clock itself — the timestamp is passed in, so
/// durations are deterministic under test.
///
/// Two rules keep it from inventing events across a gap in the data: a disconnect emits nothing,
/// and the first sample after start or after a disconnect is a silent baseline. Only transitions
/// seen between two consecutive connected samples become events.
/// </summary>
public sealed class EventLog
{
    private const int Capacity = 1000;

    private static readonly (ushort Mask, string On, string Off, EventSeverity Severity)[] Outputs =
    {
        (OutputBits.Air, "AIR closed", "AIR opened",  EventSeverity.Info),
        (OutputBits.Pre, "PRE active", "PRE off",     EventSeverity.Info),
        (OutputBits.Err, "ERR raised", "ERR cleared", EventSeverity.Critical),
    };

    private readonly List<PackEvent> _events = new();
    private readonly DateTime[] _faultOnset = new DateTime[FaultBits.Names.Length];
    private ushort _prevFaults;
    private ushort _prevOutputs;
    private bool _hasBaseline;

    public IReadOnlyList<PackEvent> Events => _events;
    public int DroppedCount { get; private set; }

    public IReadOnlyList<PackEvent> Observe(ushort faults, ushort outputs, DateTime at)
    {
        if (!_hasBaseline)
        {
            _prevFaults = faults;
            _prevOutputs = outputs;
            for (int i = 0; i < _faultOnset.Length; i++)
                if ((faults & (1 << i)) != 0) _faultOnset[i] = at;
            _hasBaseline = true;
            return Array.Empty<PackEvent>();
        }

        var emitted = new List<PackEvent>();

        ushort faultsChanged = (ushort)(faults ^ _prevFaults);
        for (int i = 0; i < _faultOnset.Length; i++)
        {
            int bit = 1 << i;
            if ((faultsChanged & bit) == 0) continue;

            if ((faults & bit) != 0)
            {
                _faultOnset[i] = at;
                emitted.Add(new PackEvent(at, PackEventType.FaultRaised, FaultBits.Names[i],
                                          null, EventSeverity.Critical));
            }
            else
            {
                emitted.Add(new PackEvent(at, PackEventType.FaultCleared, FaultBits.Names[i],
                                          at - _faultOnset[i], EventSeverity.Info));
            }
        }

        foreach (var (mask, on, off, severity) in Outputs)
        {
            if (((outputs ^ _prevOutputs) & mask) == 0) continue;
            bool nowOn = (outputs & mask) != 0;
            emitted.Add(new PackEvent(at, nowOn ? PackEventType.OutputOn : PackEventType.OutputOff,
                                      nowOn ? on : off, null, severity));
        }

        foreach (var e in emitted)
        {
            _events.Add(e);
            if (_events.Count > Capacity) { _events.RemoveAt(0); DroppedCount++; }
        }

        _prevFaults = faults;
        _prevOutputs = outputs;
        return emitted;
    }

    /// <summary>Marks a break in the data. The next Observe becomes a silent baseline.</summary>
    public void SetDisconnected() => _hasBaseline = false;

    /// <summary>Empties the visible log but keeps the baseline, so nothing re-fires as raised.</summary>
    public void Clear()
    {
        _events.Clear();
        DroppedCount = 0;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventLogTests"`
Expected: PASS — all 10 tests green.

- [ ] **Step 6: Commit**

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
git add BmsUi/Model/PackEvent.cs BmsUi/Model/EventLog.cs BmsUi.Tests/EventLogTests.cs
git commit -m "$(cat <<'EOF'
feat: EventLog — a pure latch over FAULTS and OUTPUTS

Diffs each sample against the previous one and emits a PackEvent per changed
bit into a 1000-entry ring buffer. The timestamp is injected, so fault
durations are deterministic under test. A disconnect emits nothing and every
(re)connection re-baselines silently, so no event is invented across a gap.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: CSV export

**Files:**
- Create: `BmsUi/Logging/EventCsvExporter.cs`
- Test: `BmsUi.Tests/EventCsvExporterTests.cs`

**Interfaces:**
- Consumes: `PackEvent`, `PackEventType`, `EventSeverity` from Task 1.
- Produces: `static class EventCsvExporter` with `const string Header`, `string ToCsv(IReadOnlyList<PackEvent>)`, `void Save(string path, IReadOnlyList<PackEvent>)`.

- [ ] **Step 1: Write the failing test**

Create `BmsUi.Tests/EventCsvExporterTests.cs`:

```csharp
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
            new(T0.AddSeconds(2.3), PackEventType.FaultCleared, "Cell overvoltage",
                TimeSpan.FromSeconds(2.3), EventSeverity.Info),
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventCsvExporterTests"`
Expected: FAIL — `EventCsvExporter` does not exist.

- [ ] **Step 3: Write the implementation**

Create `BmsUi/Logging/EventCsvExporter.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventCsvExporterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
git add BmsUi/Logging/EventCsvExporter.cs BmsUi.Tests/EventCsvExporterTests.cs
git commit -m "$(cat <<'EOF'
feat: EventCsvExporter — serialise the event log to CSV

Fixed header TIMESTAMP,EVENT,STATE,DURATION_MS,SEVERITY; InvariantCulture
throughout so a Turkish locale cannot corrupt the file.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: EventTimeline control

**Files:**
- Create: `BmsUi/Ui/EventTimeline.cs`
- Test: `BmsUi.Tests/UiSmokeTests.cs` (add one test; reuses the existing `RunSta` helper)

**Interfaces:**
- Consumes: `PackEvent`, `PackEventType`, `EventSeverity` from Task 1; `BmsUi.Ui.Theme`, `BmsUi.Ui.Heatmap.FromHex`.
- Produces: `sealed class EventTimeline : ListView` with `void Update(IReadOnlyList<PackEvent> events)`. Renders the chronological list newest-first; each row's `Tag` is its `PackEvent`.

- [ ] **Step 1: Write the failing test**

Add to `BmsUi.Tests/UiSmokeTests.cs`, inside the `UiSmokeTests` class (after the existing tests):

```csharp
    /// <summary>
    /// The timeline renders the chronological log newest-first and paints without throwing.
    /// Drawing logic lives in owner-draw handlers, so a construction-only test would miss it.
    /// </summary>
    [Fact]
    public void EventTimeline_ShowsEventsNewestFirstAndPaints()
    {
        RunSta(() =>
        {
            using var timeline = new EventTimeline { Width = 620, Height = 240 };
            timeline.CreateControl();

            var events = new List<PackEvent>
            {
                new(new DateTime(2026, 7, 23, 14, 0, 0), PackEventType.FaultRaised,
                    "Cell overvoltage", null, EventSeverity.Critical),
                new(new DateTime(2026, 7, 23, 14, 0, 2), PackEventType.FaultCleared,
                    "Cell overvoltage", TimeSpan.FromSeconds(2), EventSeverity.Info),
            };
            timeline.Update(events);

            Assert.Equal(2, timeline.Items.Count);
            Assert.StartsWith("14:00:02", timeline.Items[0].Text);      // newest on top
            Assert.Equal("2.0 s", timeline.Items[0].SubItems[2].Text);
            Assert.Equal("", timeline.Items[1].SubItems[2].Text);       // raised row has no duration

            using var bmp = new Bitmap(timeline.Width, timeline.Height);
            timeline.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        });
    }
```

Confirm the test file already has `using BmsUi.Model;` — if not, add it to the top of `BmsUi.Tests/UiSmokeTests.cs` alongside the existing `using` directives.

- [ ] **Step 2: Run the test to verify it fails**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventTimeline_ShowsEventsNewestFirstAndPaints"`
Expected: FAIL — `EventTimeline` does not exist.

- [ ] **Step 3: Write the control**

Create `BmsUi/Ui/EventTimeline.cs`:

```csharp
using System.Runtime.InteropServices;
using BmsUi.Model;

namespace BmsUi.Ui;

/// <summary>
/// Read-only, newest-first list of pack events. Like RegisterTable it is a ListView used only
/// for its scrolling; every pixel is painted here so it follows the dark theme. The log is
/// stored chronologically and reversed for display.
/// </summary>
public sealed class EventTimeline : ListView
{
    private const int RowHeight = 24;
    private const int CellPad = 9;
    private static readonly Color RowAlt = Heatmap.FromHex(0x1F1F1E);

    private readonly ImageList _rowSpacer = new() { ImageSize = new Size(1, RowHeight) };
    private readonly Font _timeFont = new("Consolas", 9f);
    private readonly Font _labelFont = new(Theme.FamilyName, 9.5f, FontStyle.Bold);
    private readonly Font _headerFont = new(Theme.FamilyName, 8f, FontStyle.Bold);

    public EventTimeline()
    {
        DoubleBuffered = true;
        View = View.Details;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = true;
        BorderStyle = BorderStyle.None;
        GridLines = false;
        TabStop = false;
        BackColor = Theme.Card;
        ForeColor = Theme.InkSecondary;
        Font = new Font(Theme.FamilyName, 9f);
        SmallImageList = _rowSpacer;

        Columns.Add("Time", 120, HorizontalAlignment.Left);
        Columns.Add("Event", 330, HorizontalAlignment.Left);
        Columns.Add("Duration", 110, HorizontalAlignment.Right);

        ItemSelectionChanged += (_, _) => SelectedIndices.Clear();
    }

    /// <summary>Rebuilds the rows newest-first from the chronological event list.</summary>
    public void Update(IReadOnlyList<PackEvent> events)
    {
        BeginUpdate();
        try
        {
            Items.Clear();
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i];
                var item = new ListViewItem(e.At.ToString("HH:mm:ss.fff")) { Tag = e };
                item.SubItems.Add($"{Icon(e.Type)}  {e.Label}");
                item.SubItems.Add(e.Duration is { } d ? $"{d.TotalSeconds:F1} s" : "");
                Items.Add(item);
            }
        }
        finally { EndUpdate(); }
    }

    private static string Icon(PackEventType type) => type switch
    {
        PackEventType.FaultRaised => "▲",
        PackEventType.FaultCleared => "▼",
        _ => "●",
    };

    // ---------------- theme + drawing (mirrors RegisterTable) ----------------

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string appName, IntPtr idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The scroll bar is system-drawn; opt the window into the dark explorer theme to
        // reach it. Undocumented name, a no-op on versions that do not know it.
        SetWindowTheme(Handle, "DarkMode_Explorer", IntPtr.Zero);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(Theme.Page);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var hairline = new Pen(Theme.Hairline);
        e.Graphics.DrawLine(hairline, e.Bounds.Left, e.Bounds.Bottom - 1,
                            e.Bounds.Right, e.Bounds.Bottom - 1);
        DrawCell(e.Graphics, e.Header!.Text.ToUpperInvariant(), e.Bounds, _headerFont,
                 Theme.InkMuted, e.Header.TextAlign);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        using var fill = new SolidBrush(e.ItemIndex % 2 == 0 ? Theme.Card : RowAlt);
        e.Graphics.FillRectangle(fill, e.Bounds);
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var ev = (PackEvent)e.Item!.Tag!;
        (Font font, Color ink) = e.ColumnIndex switch
        {
            0 => (_timeFont, Theme.InkMuted),
            1 => (_labelFont, ev.Severity == EventSeverity.Critical ? Theme.Critical : Theme.InkSecondary),
            _ => (Font, Theme.InkSecondary),
        };
        DrawCell(e.Graphics, e.SubItem!.Text, e.Bounds, font, ink, Columns[e.ColumnIndex].TextAlign);
    }

    private static void DrawCell(Graphics g, string text, Rectangle bounds, Font font,
                                 Color ink, HorizontalAlignment align)
    {
        if (text.Length == 0) return;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | align switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left,
        };
        TextRenderer.DrawText(g, text, font, Rectangle.Inflate(bounds, -CellPad, 0), ink, flags);
    }

    // The Event column absorbs the leftover width so the table never ends in dead space.
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (Columns.Count < 3) return;
        Columns[1].Width = Math.Max(200, ClientSize.Width - Columns[0].Width - Columns[2].Width);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SmallImageList = null;
            _rowSpacer.Dispose();
            _timeFont.Dispose();
            _labelFont.Dispose();
            _headerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test --filter "FullyQualifiedName~EventTimeline_ShowsEventsNewestFirstAndPaints"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
git add BmsUi/Ui/EventTimeline.cs BmsUi.Tests/UiSmokeTests.cs
git commit -m "$(cat <<'EOF'
feat: EventTimeline — owner-drawn, newest-first event list

Drawn like RegisterTable: a ListView kept only for scrolling, every pixel
painted in the dark theme with the dark scroll bar. Fault rows are coloured
by severity; the Event column absorbs the leftover width.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Wire the Events tab into Form1

**Files:**
- Modify: `BmsUi/Form1.Designer.cs` (field declarations near line 39; the registers-tab block near line 288; the `TabPages.AddRange` near line 337)
- Modify: `BmsUi/Form1.cs` (field near line 14; `Disconnect` near line 190; `ApplySnapshot` near line 274; accessors near line 402; new button handlers)
- Modify: `docs/design-decisions.md`, `README.md`
- Test: relies on the existing `EveryTab_PaintsWithNoSystemChromeLeft` (auto-covers the new tab) plus one added accessor assertion in `BmsUi.Tests/UiSmokeTests.cs`

**Interfaces:**
- Consumes: `EventLog`, `EventTimeline`, `EventCsvExporter` from Tasks 1–3; `BmsSnapshot.Faults`, `BmsSnapshot.Outputs` (existing `ushort` properties).
- Produces: `Form1.EventsTab` (`TabPage`) and `Form1.EventTimelineControl` (`EventTimeline`) internal accessors for tests.

- [ ] **Step 1: Declare the Designer fields**

In `BmsUi/Form1.Designer.cs`, find:

```csharp
    private TabPage voltageTab, temperatureTab, balanceTab, registersTab, settingsTab, logTab;
    private RegisterTable registersTable;
```

Replace with:

```csharp
    private TabPage voltageTab, temperatureTab, balanceTab, registersTab, eventsTab, settingsTab, logTab;
    private RegisterTable registersTable;
    private EventTimeline eventTimeline;
    private Button clearEventsButton, exportEventsButton;
    private Label eventCountLabel;
```

- [ ] **Step 2: Build the Events tab**

In `BmsUi/Form1.Designer.cs`, find the end of the registers-tab block:

```csharp
        registersTab = new TabPage("Registers");
        registersTab.Controls.Add(registersTable);
        registersTab.Controls.Add(registersNoteLabel);
```

Immediately after it, insert:

```csharp
        // ---------------- events tab ----------------
        eventTimeline = new EventTimeline { Dock = DockStyle.Fill };

        clearEventsButton = new Button { Text = "Clear", Location = new Point(6, 7), Width = 90 };
        clearEventsButton.Click += clearEventsButton_Click;
        exportEventsButton = new Button { Text = "Export...", Location = new Point(102, 7), Width = 110 };
        exportEventsButton.Click += exportEventsButton_Click;
        eventCountLabel = new Label
        {
            AutoSize = true,
            Location = new Point(226, 13),
            Text = "0 events · 0 older dropped",
        };
        var eventButtons = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        eventButtons.Controls.Add(clearEventsButton);
        eventButtons.Controls.Add(exportEventsButton);
        eventButtons.Controls.Add(eventCountLabel);

        eventsTab = new TabPage("Events");
        eventsTab.Controls.Add(eventTimeline);        // Fill added first, matching the registers tab
        eventsTab.Controls.Add(eventButtons);
```

- [ ] **Step 3: Add the tab to the strip**

In `BmsUi/Form1.Designer.cs`, find:

```csharp
        tabs.TabPages.AddRange(new[]
        {
            voltageTab, temperatureTab, balanceTab, registersTab, settingsTab, logTab,
        });
```

Replace the array line with:

```csharp
            voltageTab, temperatureTab, balanceTab, registersTab, eventsTab, settingsTab, logTab,
```

- [ ] **Step 4: Declare the EventLog field**

In `BmsUi/Form1.cs`, find:

```csharp
    private CsvLogger? _logger;
```

Add on the next line:

```csharp
    private readonly EventLog _eventLog = new();
```

- [ ] **Step 5: Feed the log on every snapshot**

In `BmsUi/Form1.cs`, find in `ApplySnapshot`:

```csharp
        registersTable.UpdateData(s);

        UpdateDashboard(s);
```

Replace with:

```csharp
        registersTable.UpdateData(s);

        // The latch runs on every snapshot regardless of the visible tab — catching an event
        // nobody is watching is the whole point. It is only repainted when something changed.
        // The validity gate makes the first *valid* read the baseline, so a fault already
        // active when the link comes up is not mistaken for one that just appeared.
        if (s.RegisterValid[Reg.Faults] && s.RegisterValid[Reg.Outputs] &&
            _eventLog.Observe(s.Faults, s.Outputs, DateTime.Now).Count > 0)
        {
            eventTimeline.Update(_eventLog.Events);
            eventCountLabel.Text = $"{_eventLog.Events.Count} events · {_eventLog.DroppedCount} older dropped";
        }

        UpdateDashboard(s);
```

- [ ] **Step 6: Re-baseline on disconnect**

In `BmsUi/Form1.cs`, find in `Disconnect`:

```csharp
        if (statusLabel.ForeColor != Theme.Critical) SetStatus("Not connected", Theme.InkMuted);
        UpdateDashboard(null);
```

Replace with:

```csharp
        if (statusLabel.ForeColor != Theme.Critical) SetStatus("Not connected", Theme.InkMuted);
        _eventLog.SetDisconnected();   // a gap in the data must not read as every fault clearing
        UpdateDashboard(null);
```

- [ ] **Step 7: Add the button handlers**

In `BmsUi/Form1.cs`, find the CSV-log region marker:

```csharp
    // ------------------------------------------------------------------ CSV log
```

Immediately before it, insert:

```csharp
    // ------------------------------------------------------------------ events

    private void clearEventsButton_Click(object? sender, EventArgs e)
    {
        _eventLog.Clear();
        eventTimeline.Update(_eventLog.Events);
        eventCountLabel.Text = "0 events · 0 older dropped";
    }

    private void exportEventsButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"bmsui_events_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            EventCsvExporter.Save(dialog.FileName, _eventLog.Events);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not write the file: {ex.Message}", "Export failed");
        }
    }
```

- [ ] **Step 8: Expose test accessors**

In `BmsUi/Form1.cs`, find:

```csharp
    internal RegisterTable RegistersTable => registersTable;
    internal TabPage RegistersTab => registersTab;
```

Add on the next lines:

```csharp
    internal EventTimeline EventTimelineControl => eventTimeline;
    internal TabPage EventsTab => eventsTab;
```

- [ ] **Step 9: Add the accessor assertion to the smoke suite**

In `BmsUi.Tests/UiSmokeTests.cs`, add this test inside the `UiSmokeTests` class:

```csharp
    /// <summary>The Events tab exists, is in the strip, and starts empty.</summary>
    [Fact]
    public void EventsTab_IsPresentAndStartsEmpty()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Show();
            Application.DoEvents();

            Assert.Contains(form.EventsTab, form.TabsControl.TabPages.Cast<TabPage>());
            Assert.Empty(form.EventTimelineControl.Items);

            form.Close();
        });
    }
```

- [ ] **Step 10: Build, test, and verify the tab renders**

Run: `export PATH="/c/Program Files/dotnet:$PATH" && dotnet test`
Expected: PASS — full suite green, including `EventsTab_IsPresentAndStartsEmpty` and `EveryTab_PaintsWithNoSystemChromeLeft` (which now also covers the Events tab).

Run: close the app if open, then `export PATH="/c/Program Files/dotnet:$PATH" && dotnet build -c Release --no-incremental`
Expected: `0 Uyarı, 0 Hata`.

- [ ] **Step 11: Update the docs**

In `README.md`, find the list of tabs / features and add a bullet describing the Events tab. Exact text to add:

```markdown
- **Events** — a host-side timeline of every FAULTS bit and AIR/PRE/ERR transition, timestamped, with the duration each fault stayed set. In-memory; export to CSV from the tab. Read-only, like the rest of the app.
```

In `docs/design-decisions.md`, under `## 2. Architecture`, add this bullet (the section lists the pure, testable cores of the app):

```markdown
- **`EventLog`** is a pure diff over the FAULTS and OUTPUTS registers, the same shape as
  `PollSchedule` and `CellAnalysis`: the timestamp is injected, so fault durations are
  deterministic under test. The one impurity — `DateTime.Now` — stays at the call site in
  `Form1`. A disconnect re-baselines it so a gap in the data never reads as every fault
  clearing at once.
```

- [ ] **Step 12: Commit**

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
git add BmsUi/Form1.Designer.cs BmsUi/Form1.cs BmsUi.Tests/UiSmokeTests.cs README.md docs/design-decisions.md
git commit -m "$(cat <<'EOF'
feat: Events tab — live fault & output timeline

The EventLog runs on every snapshot regardless of the visible tab, and
re-baselines on disconnect. The tab shows the timeline newest-first with
Clear and Export buttons and a running count. FAULTS and OUTPUTS are already
polled at 10 Hz, so the feature adds no bus traffic.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Visual confirmation

**Files:**
- Create (temporary): `BmsUi.Tests/PreviewScratch.cs` — deleted before the final commit
- No production files change in this task.

**Interfaces:**
- Consumes: `Form1.TabsControl`, `Form1.EventsTab`, `Form1.SimulationCheckBox`, `Form1.StartStopButton` (all existing internal accessors).

- [ ] **Step 1: Render the Events tab under simulation**

The simulator rotates FAULTS every 12 s (clean → overvoltage → overtemperature → precharge timeout) and toggles PRE/AIR/ERR, so a ~26 s run produces raised and cleared events. Create `BmsUi.Tests/PreviewScratch.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;
using BmsUi;
using Xunit;

public class PreviewScratch
{
    [Fact]
    public void RenderEventsTab()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form1();
                form.Size = new Size(1500, 950);
                form.Show();
                Application.DoEvents();

                form.TabsControl.SelectedTab = form.EventsTab;
                form.SimulationCheckBox.Checked = true;
                form.StartStopButton.PerformClick();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 26000)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(Environment.GetEnvironmentVariable("PREVIEW_OUT")!,
                         System.Drawing.Imaging.ImageFormat.Png);

                form.StartStopButton.PerformClick();
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception(failure.ToString());
    }
}
```

Run:
```bash
export PATH="/c/Program Files/dotnet:$PATH"
export PREVIEW_OUT="C:/Users/burem/AppData/Local/Temp/claude/C--Users-burem-STM32CubeIDE-workspace-1-19-0-lvbmsgui-main/ff732c0c-5768-4841-86fe-6ba00418d114/scratchpad/events.png"
dotnet test --filter "FullyQualifiedName~PreviewScratch"
```
Expected: PASS, and `events.png` written.

- [ ] **Step 2: Inspect the PNG**

Open `events.png`. Confirm: the Events tab is fully dark (no grey strip behind the buttons), rows are newest-first, raised rows are red, cleared rows carry a duration like `12.0 s`, and the count label reads a non-zero "N events · 0 older dropped". If anything is off, fix it and re-render before continuing.

- [ ] **Step 3: Delete the scratch file and confirm the suite is clean**

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
rm -f BmsUi.Tests/PreviewScratch.cs
export PATH="/c/Program Files/dotnet:$PATH" && dotnet test
```
Expected: PASS — full suite green, no PreviewScratch.

- [ ] **Step 4: Commit (only if Step 2 required a production fix)**

If Step 2 was clean, there is nothing to commit here — the scratch file was never committed. If a fix was needed, commit it:

```bash
cd "C:/Users/burem/STM32CubeIDE/workspace_1.19.0/bmsui-v2"
git add -A
git commit -m "$(cat <<'EOF'
fix: <describe the Events-tab rendering fix found during visual review>

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- Purpose / "what happened, and when" → Tasks 1 (log) + 4 (live wiring). ✓
- Scope: FAULTS + OUTPUTS only, 10 Hz, always-on → Task 4 Step 5 (Observe on every snapshot, not gated on the tab). ✓
- Resolution limit (sub-100 ms invisible) → inherent to polling; recorded in the spec, no code owes it. ✓
- Event kinds (FaultRaised/Cleared, OutputOn/Off; labels; severity) → Task 1 (EventLog) + tests. ✓
- Data model (`PackEvent`, enums) → Task 1 Step 3. ✓
- EventLog surface + ring buffer + baseline flag → Task 1 Step 4, tested Step 1. ✓
- Connection handling (disconnect emits nothing; silent re-baseline) → Task 1 (`SetDisconnected`), Task 4 Step 6 (call site); tests `ReconnectReBaselinesSilently`, `Disconnect_DoesNotEmitClears`. ✓
- UI (new Events tab, owner-drawn like Registers, newest-first, columns, Clear/Export, count line) → Tasks 3 + 4. ✓
- Export (fixed header, InvariantCulture, save dialog, manual) → Task 2 + Task 4 Step 7. ✓
- Simulator unchanged → confirmed; Task 5 relies on its existing fault rotation. ✓
- Testing (EventLog pure, export, UI smoke) → Tasks 1, 2, 3, 4. ✓
- Out of scope (no writes, no connection/threshold events, no auto-persist, no CHARGING_STATE) → nothing in the plan adds them. ✓

**Placeholder scan:** none — every code step carries complete code; the only free-text is Task 5 Step 4's commit subject, which is conditional on a fix actually being needed.

**Type consistency:** `PackEvent(DateTime, PackEventType, string, TimeSpan?, EventSeverity)` is used identically in Tasks 1–3. `Observe(ushort, ushort, DateTime) : IReadOnlyList<PackEvent>`, `SetDisconnected()`, `Clear()`, `Events`, `DroppedCount` match between the EventLog definition (Task 1) and every call site (Task 4). `EventTimeline.Update(IReadOnlyList<PackEvent>)` matches its call sites. `EventCsvExporter.Save(string, IReadOnlyList<PackEvent>)` / `ToCsv(IReadOnlyList<PackEvent>)` / `Header` match Tasks 2 and 4. Accessor names `EventsTab` / `EventTimelineControl` match between Task 4 Step 8 and Task 4 Step 9 / Task 5.
