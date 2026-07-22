# Register Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:executing-plans` to implement
> this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A read-only Registers tab listing every readable MAINBUFFER index with its name, raw
value and scaled value, refreshed at 1 Hz only while the tab is visible.

**Architecture:** A pure `RegisterCatalog` holds index → (name, unit, scale, sign, group) and
does all formatting. `PollSchedule`/`PollWorker` gain an opt-in sweep of the indices that are
not already polled. A `RegisterTable` control (ListView, Details) renders rows from the
existing `BmsSnapshot.Registers` / `RegisterValid` arrays — no new model type.

**Tech Stack:** C# .NET 10 WinForms, xUnit. Project root:
`C:\Users\burem\STM32CubeIDE\workspace_1.19.0\bmsui-v2`

## Global Constraints

- Read-only: the sweep uses `SerialLink.ReadRegister` only; nothing writes to the device.
- Never poll indices 41/42/43 (shadowed by `0x29`/`0x2A`/`0x2B`) or ≥ 50 —
  `SerialLink.ReadRegister` throws `ArgumentOutOfRangeException` for those.
- All code, comments and UI strings in English.
- `dotnet build -c Release` must stay at 0 warnings, `dotnet test` fully green.
- Close the running app before a Release build; a build against a locked exe leaves
  `bin/Release` half-populated.

## File Structure

| File | Responsibility |
|---|---|
| `BmsUi/Protocol/RegisterCatalog.cs` | **new** — descriptors for every readable index + value/raw/note formatting |
| `BmsUi.Tests/RegisterCatalogTests.cs` | **new** — catalog contents, scaling, signedness, formatting |
| `BmsUi/Polling/PollSchedule.cs` | add `PollItem.AllRegisters` and the `SweepRegisters` set |
| `BmsUi/Polling/PollWorker.cs` | add `IncludeAllRegisters`; handle the new poll item |
| `BmsUi.Tests/PollScheduleTests.cs` | sweep cadence and "no shadowed index" guarantee |
| `BmsUi/Ui/RegisterTable.cs` | **new** — ListView-based table, change highlighting |
| `BmsUi/Form1.Designer.cs` | new Registers tab hosting the table |
| `BmsUi/Form1.cs` | feed snapshots to the table; toggle the sweep from tab selection |
| `BmsUi/Serial/SimulatedTransport.cs` | plausible charger register values |
| `BmsUi.Tests/UiSmokeTests.cs` | tab exists; table fills under simulation |

---

### Task 1: Register catalog

**Files:**
- Create: `BmsUi/Protocol/RegisterCatalog.cs`
- Create: `BmsUi.Tests/RegisterCatalogTests.cs`

**Interfaces:**
- Consumes: `Reg.*` constants and `HvProtocol.IsValidRegister` from `BmsUi/Protocol/HvProtocol.cs`
- Produces:
  - `enum RegisterGroup { Status, Charger, Pack, Config, Unnamed }`
  - `readonly record struct RegisterDescriptor(byte Index, string Name, string Unit, double Scale, bool Signed, RegisterGroup Group)` with `bool IsKnown` and `bool IsBitMask`
  - `static IReadOnlyList<RegisterDescriptor> RegisterCatalog.All` — every readable index, in group order
  - `static RegisterDescriptor RegisterCatalog.Describe(byte index)`
  - `static string RegisterCatalog.FormatRaw(byte index, ushort raw)`
  - `static string RegisterCatalog.FormatValue(byte index, ushort raw)`
  - `static string RegisterCatalog.FormatNote(byte index, ushort raw)`

- [ ] **Step 1: Write the failing tests** — `BmsUi.Tests/RegisterCatalogTests.cs`

```csharp
using BmsUi.Protocol;
using Xunit;

public class RegisterCatalogTests
{
    [Fact]
    public void All_CoversEveryReadableIndex_AndNothingElse()
    {
        var indices = RegisterCatalog.All.Select(d => (int)d.Index).ToList();

        // 0..40 and 44..49 — 41/42/43 are shadowed by commands, >=50 is dropped
        Assert.Equal(47, indices.Count);
        Assert.Equal(indices.Count, indices.Distinct().Count());
        Assert.DoesNotContain(41, indices);
        Assert.DoesNotContain(42, indices);
        Assert.DoesNotContain(43, indices);
        Assert.All(RegisterCatalog.All, d => Assert.True(HvProtocol.IsValidRegister(d.Index)));
    }

    [Fact]
    public void Describe_KnownRegister_CarriesNameUnitAndScale()
    {
        var d = RegisterCatalog.Describe(Reg.PackVoltage);
        Assert.Equal("PACK_VOLTAGE", d.Name);
        Assert.Equal("V", d.Unit);
        Assert.Equal(100.0, d.Scale);
        Assert.False(d.Signed);
        Assert.True(d.IsKnown);
    }

    [Fact]
    public void Describe_UnnamedRegister_IsStillWellFormed()
    {
        var d = RegisterCatalog.Describe(19);
        Assert.False(d.IsKnown);
        Assert.Equal(RegisterGroup.Unnamed, d.Group);
        Assert.Equal(1.0, d.Scale);
    }

    [Fact]
    public void FormatValue_SignedRegisters_ShowNegatives()
    {
        // PACK_CURRENT: signed, x10 -> -37.5 A
        Assert.Equal("-37.5 A", RegisterCatalog.FormatValue(Reg.PackCurrent, unchecked((ushort)-375)));
        // CHARGE_OVER_CURRENT_TRESHOLD: firmware init is -500 -> -50.0 A
        Assert.Equal("-50.0 A", RegisterCatalog.FormatValue(34, unchecked((ushort)-500)));
        // MIN_CELL_TEMP: signed, x100 -> -12.50 C
        Assert.Equal("-12.50 °C", RegisterCatalog.FormatValue(Reg.MinCellTemp, unchecked((ushort)-1250)));
    }

    [Fact]
    public void FormatValue_UnsignedRegisters_UseTheirScale()
    {
        Assert.Equal("374.41 V", RegisterCatalog.FormatValue(Reg.PackVoltage, 37441));
        Assert.Equal("6.0 A", RegisterCatalog.FormatValue(4, 60));          // CHARGER_SET_CURRENT
        Assert.Equal("73.00 %", RegisterCatalog.FormatValue(Reg.EstimatedSoc, 7300));
        Assert.Equal("5 mV", RegisterCatalog.FormatValue(Reg.AllowedDisbalance, 5));
        Assert.Equal("100 ms", RegisterCatalog.FormatValue(36, 100));       // OVER_VOLTAGE_ERROR_DELAY
    }

    [Fact]
    public void FormatValue_BitMaskRegisters_ShowHexNotAScaledNumber()
    {
        Assert.Equal("0x0006", RegisterCatalog.FormatValue(Reg.Faults, 0x0006));
        Assert.Equal("0x0003", RegisterCatalog.FormatValue(Reg.Outputs, 0x0003));
    }

    [Fact]
    public void FormatRaw_ShowsDecimal_AndHexForBitMasks()
    {
        Assert.Equal("37441", RegisterCatalog.FormatRaw(Reg.PackVoltage, 37441));
        Assert.Equal("6  (0x0006)", RegisterCatalog.FormatRaw(Reg.Faults, 6));
    }

    [Fact]
    public void FormatNote_DecodesFaultBits()
    {
        string note = RegisterCatalog.FormatNote(Reg.Faults, (1 << 2) | (1 << 13));
        Assert.Contains("Cell overvoltage", note);
        Assert.Contains("Precharge timeout", note);
    }

    [Fact]
    public void FormatNote_DecodesOutputsAndChargingState()
    {
        Assert.Contains("AIR", RegisterCatalog.FormatNote(Reg.Outputs, OutputBits.Air));
        // Firmware enum: 1 NO_CHARGER, 2 CHARGING, 3 BALANCING, 4 COMPLETED, 5 ERROR
        Assert.Contains("CHARGING", RegisterCatalog.FormatNote(2, 2));
    }

    [Fact]
    public void FormatNote_NoFaults_SaysSo()
        => Assert.Equal("none", RegisterCatalog.FormatNote(Reg.Faults, 0));

    [Fact]
    public void Groups_AreOrdered_StatusChargerPackConfigUnnamed()
    {
        var order = RegisterCatalog.All.Select(d => d.Group).ToList();
        var expected = order.OrderBy(g => (int)g).ToList();
        Assert.Equal(expected, order);
        Assert.Equal(RegisterGroup.Charger, RegisterCatalog.Describe(5).Group);
        Assert.Equal(RegisterGroup.Config, RegisterCatalog.Describe(33).Group);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter RegisterCatalogTests`
Expected: compile error — `RegisterCatalog` does not exist.

- [ ] **Step 3: Write the catalog** — `BmsUi/Protocol/RegisterCatalog.cs`

```csharp
using System.Globalization;

namespace BmsUi.Protocol;

public enum RegisterGroup { Status, Charger, Pack, Config, Unnamed }

public readonly record struct RegisterDescriptor(
    byte Index, string Name, string Unit, double Scale, bool Signed, RegisterGroup Group)
{
    public bool IsKnown => Name.Length > 0;

    /// <summary>FAULTS and OUTPUTS carry bit fields, so a scaled number is meaningless.</summary>
    public bool IsBitMask => Index is Reg.Faults or Reg.Outputs;

    /// <summary>Decimals implied by the scale: 100 -> 2, 10 -> 1, 1 -> 0.</summary>
    public int Decimals => Scale >= 100 ? 2 : Scale >= 10 ? 1 : 0;
}

/// <summary>
/// Index -> meaning for every readable MAINBUFFER register: name, unit, scale and sign.
///
/// The same knowledge lives implicitly inside BmsSnapshot's properties. It is duplicated here
/// as data because the inspector needs it for indices BmsSnapshot has no property for, and
/// because a table is the natural shape for it. BmsSnapshot is intentionally left alone.
///
/// Scales come from the firmware: main.h:93-123 for the indices, Initialize_System
/// (main.cpp:466-482) for the units the init values imply, and the delay registers are
/// compared against FreeRTOS ticks (main.cpp:699) so they are milliseconds.
/// </summary>
public static class RegisterCatalog
{
    private static readonly Dictionary<byte, RegisterDescriptor> Known = BuildKnown();

    public static IReadOnlyList<RegisterDescriptor> All { get; } = BuildAll();

    public static RegisterDescriptor Describe(byte index)
        => Known.TryGetValue(index, out var d)
           ? d
           : new RegisterDescriptor(index, "", "", 1.0, false, RegisterGroup.Unnamed);

    public static string FormatRaw(byte index, ushort raw)
    {
        var d = Describe(index);
        return d.IsBitMask
            ? $"{raw}  (0x{raw:X4})"
            : raw.ToString(CultureInfo.CurrentCulture);
    }

    public static string FormatValue(byte index, ushort raw)
    {
        var d = Describe(index);
        if (d.IsBitMask) return $"0x{raw:X4}";

        double value = (d.Signed ? (short)raw : raw) / d.Scale;
        string number = value.ToString($"F{d.Decimals}", CultureInfo.CurrentCulture);
        return d.Unit.Length == 0 ? number : $"{number} {d.Unit}";
    }

    public static string FormatNote(byte index, ushort raw)
    {
        if (index == Reg.Faults)
        {
            var names = FaultBits.Decode(raw);
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
        if (index == Reg.Outputs)
        {
            var on = new List<string>(3);
            if ((raw & OutputBits.Air) != 0) on.Add("AIR");
            if ((raw & OutputBits.Pre) != 0) on.Add("PRE");
            if ((raw & OutputBits.Err) != 0) on.Add("ERR");
            return on.Count == 0 ? "all open" : string.Join(" + ", on);
        }
        if (index == 2)
        {
            // Firmware enum CHARG_STATE (main.cpp:285-291). Note: the firmware writes this
            // register before assigning the value, so in practice it always reads NO_CHARGER.
            return raw switch
            {
                1 => "NO_CHARGER",
                2 => "CHARGING",
                3 => "BALANCING",
                4 => "CHARGING_COMPLETED",
                5 => "CHARGING_ERROR",
                _ => "",
            };
        }
        return "";
    }

    private static Dictionary<byte, RegisterDescriptor> BuildKnown()
    {
        var list = new List<RegisterDescriptor>
        {
            new(Reg.Faults,  "FAULTS",  "", 1,   false, RegisterGroup.Status),
            new(Reg.Outputs, "OUTPUTS", "", 1,   false, RegisterGroup.Status),

            new(2,  "CHARGING_STATE",         "",  1,   false, RegisterGroup.Charger),
            new(3,  "CHARGER_SET_VOLTAGE",    "V", 100, false, RegisterGroup.Charger),
            new(4,  "CHARGER_SET_CURRENT",    "A", 10,  false, RegisterGroup.Charger),
            new(5,  "CHARGER_ACTUAL_VOLTAGE", "V", 100, false, RegisterGroup.Charger),
            new(6,  "CHARGER_ACTUAL_CURRENT", "A", 10,  false, RegisterGroup.Charger),
            new(31, "CHARGE_CURRENT",         "A", 10,  true,  RegisterGroup.Charger),

            new(Reg.PackVoltage,      "PACK_VOLTAGE",       "V",  100, false, RegisterGroup.Pack),
            new(Reg.PackCurrent,      "PACK_CURRENT",       "A",  10,  true,  RegisterGroup.Pack),
            new(Reg.MaxCellVoltage,   "MAX_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.MinCellVoltage,   "MIN_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.TotalCellVoltage, "TOTAL_CELL_VOLTAGE", "V",  100, false, RegisterGroup.Pack),
            new(Reg.MaxCellTemp,      "MAX_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.MinCellTemp,      "MIN_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.AvgCellVoltage,   "AVG_CELL_VOLTAGE",   "V",  100, false, RegisterGroup.Pack),
            new(Reg.AvgCellTemp,      "AVG_CELL_TEMP",      "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.MaxSlaveTemp,     "MAX_SLAVE_TEMP",     "°C", 100, true,  RegisterGroup.Pack),
            new(Reg.EstimatedSoc,     "ESTIMATED_SoC",      "%",  100, false, RegisterGroup.Pack),
            new(18, "OPEN_CIRCUIT_VOLTAGE", "V", 100, false, RegisterGroup.Pack),

            new(Reg.AllowedDisbalance,   "ALLOWED_DISBALANCE",             "mV", 1,  false, RegisterGroup.Config),
            new(Reg.PrechargePercentage, "PRECHARGE_PERCENTAGE",           "%",  1,  false, RegisterGroup.Config),
            new(Reg.PrechargeTimeout,    "PRECHARGE_TIMEOUT",              "ms", 1,  false, RegisterGroup.Config),
            new(34, "CHARGE_OVER_CURRENT_TRESHOLD",    "A",  10, true,  RegisterGroup.Config),
            new(35, "DISCHARGE_OVER_CURRENT_TRESHOLD", "A",  10, true,  RegisterGroup.Config),
            new(36, "OVER_VOLTAGE_ERROR_DELAY",        "ms", 1,  false, RegisterGroup.Config),
            new(37, "UNDER_VOLTAGE_ERROR_DELAY",       "ms", 1,  false, RegisterGroup.Config),
            new(38, "OVER_CURRENT_ERROR_DELAY",        "ms", 1,  false, RegisterGroup.Config),
            new(39, "OPEN_WIRE_ERROR_DELAY",           "ms", 1,  false, RegisterGroup.Config),
            new(40, "HEAT_ERROR_DELAY",                "ms", 1,  false, RegisterGroup.Config),
        };
        return list.ToDictionary(d => d.Index);
    }

    private static IReadOnlyList<RegisterDescriptor> BuildAll()
    {
        var all = new List<RegisterDescriptor>();
        for (byte i = 0; i < HvProtocol.MaxRegisterIndexExclusive; i++)
        {
            if (!HvProtocol.IsValidRegister(i)) continue;   // skips 41/42/43
            all.Add(Describe(i));
        }
        return all.OrderBy(d => (int)d.Group).ThenBy(d => d.Index).ToList();
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --filter RegisterCatalogTests`
Expected: PASS (11 tests).

> If `All_CoversEveryReadableIndex_AndNothingElse` fails on the count, print
> `RegisterCatalog.All.Count` and check `HvProtocol.IsValidRegister` — 0..40 plus 44..49 is 47.

- [ ] **Step 5: Commit**

```bash
git add BmsUi/Protocol/RegisterCatalog.cs BmsUi.Tests/RegisterCatalogTests.cs
git commit -m "feat: register catalog with names, scales and formatting"
```

---

### Task 2: Opt-in register sweep

**Files:**
- Modify: `BmsUi/Polling/PollSchedule.cs`
- Modify: `BmsUi/Polling/PollWorker.cs`
- Modify: `BmsUi.Tests/PollScheduleTests.cs`

**Interfaces:**
- Consumes: `RegisterCatalog.All` from Task 1
- Produces:
  - `PollItem.AllRegisters` enum member
  - `static readonly byte[] PollSchedule.SweepRegisters`
  - `bool PollWorker.IncludeAllRegisters { get; set; }`

- [ ] **Step 1: Write the failing tests** — append to `BmsUi.Tests/PollScheduleTests.cs`

```csharp
    [Fact]
    public void SweepRegisters_ExcludeTheOnesAlreadyPolled()
    {
        // Asking again for values refreshed at 5-10 Hz would be redundant traffic
        var regular = PollSchedule.FastRegisters
            .Concat(PollSchedule.SummaryRegisters)
            .Concat(PollSchedule.ConfigRegisters)
            .ToHashSet();

        Assert.All(PollSchedule.SweepRegisters, idx => Assert.DoesNotContain(idx, regular));
    }

    [Fact]
    public void SweepRegisters_CoverEveryOtherReadableIndex()
    {
        var covered = PollSchedule.FastRegisters
            .Concat(PollSchedule.SummaryRegisters)
            .Concat(PollSchedule.ConfigRegisters)
            .Concat(PollSchedule.SweepRegisters)
            .ToHashSet();

        Assert.Equal(RegisterCatalog.All.Count, covered.Count);
        Assert.All(RegisterCatalog.All, d => Assert.Contains(d.Index, covered));
    }

    [Fact]
    public void SweepRegisters_AreAllReadable()
        => Assert.All(PollSchedule.SweepRegisters,
                      idx => Assert.True(HvProtocol.IsValidRegister(idx)));

    [Fact]
    public void AllRegisters_IsScheduledEveryTenthTick_GivingOneHz()
    {
        Assert.Contains(PollItem.AllRegisters, PollSchedule.ItemsForTick(0));
        Assert.Contains(PollItem.AllRegisters, PollSchedule.ItemsForTick(10));
        Assert.DoesNotContain(PollItem.AllRegisters, PollSchedule.ItemsForTick(5));
    }
```

Add `using BmsUi.Protocol;` to the top of the file if it is not already there.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --filter PollScheduleTests`
Expected: compile error — `SweepRegisters` and `PollItem.AllRegisters` do not exist.

- [ ] **Step 3: Extend `PollSchedule`** — `BmsUi/Polling/PollSchedule.cs`

Replace the enum and add the sweep set:

```csharp
public enum PollItem { FastRegisters, SummaryRegisters, CellVoltages, CellTemps, Balance, AllRegisters }
```

Add inside `PollSchedule`, after `ConfigRegisters`:

```csharp
    /// <summary>
    /// Everything the regular schedule does not already cover, for the Registers tab.
    /// Only swept while that tab is visible; the values it does not include are refreshed at
    /// 5-10 Hz anyway and are already in the snapshot.
    /// </summary>
    public static readonly byte[] SweepRegisters = BuildSweep();

    private static byte[] BuildSweep()
    {
        var regular = FastRegisters.Concat(SummaryRegisters).Concat(ConfigRegisters).ToHashSet();
        return RegisterCatalog.All
            .Select(d => d.Index)
            .Where(idx => !regular.Contains(idx))
            .ToArray();
    }
```

And schedule it inside `ItemsForTick`, right before the `return`:

```csharp
        if (tick % 10 == 0) items.Add(PollItem.AllRegisters);
```

- [ ] **Step 4: Extend `PollWorker`** — `BmsUi/Polling/PollWorker.cs`

Add the property next to the other public members:

```csharp
    /// <summary>
    /// Set while the Registers tab is visible. Off by default so the extra sweep costs
    /// nothing when nobody is looking at it.
    /// </summary>
    public bool IncludeAllRegisters { get; set; }
```

Add the case to `Poll`:

```csharp
            case PollItem.AllRegisters:
                if (!IncludeAllRegisters) break;
                foreach (byte idx in PollSchedule.SweepRegisters) PollRegister(idx);
                break;
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS. The existing `TransactionsPerSecond_MatchesDesignBudget` test still passes
because `AllRegisters` contributes nothing to its count while the flag is off — if it fails,
make that test count `PollItem.AllRegisters` as 0.

- [ ] **Step 6: Commit**

```bash
git add BmsUi/Polling BmsUi.Tests/PollScheduleTests.cs
git commit -m "feat: opt-in register sweep, off unless the Registers tab is visible"
```

---

### Task 3: Registers tab

**Files:**
- Create: `BmsUi/Ui/RegisterTable.cs`
- Modify: `BmsUi/Form1.Designer.cs`
- Modify: `BmsUi/Form1.cs`
- Modify: `BmsUi/Serial/SimulatedTransport.cs`
- Modify: `BmsUi.Tests/UiSmokeTests.cs`

**Interfaces:**
- Consumes: `RegisterCatalog` (Task 1), `PollWorker.IncludeAllRegisters` (Task 2),
  `BmsSnapshot.Registers` / `BmsSnapshot.RegisterValid`
- Produces: `RegisterTable.UpdateData(BmsSnapshot? snapshot)`, `Form1.RegistersTable` (internal,
  for tests)

- [ ] **Step 1: Write the table control** — `BmsUi/Ui/RegisterTable.cs`

```csharp
using System.Diagnostics;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Ui;

/// <summary>
/// Read-only table of every readable MAINBUFFER register.
///
/// A ListView rather than an owner-drawn control: 47 rows do not fit the tab, and ListView
/// brings scrolling for free. Owner-drawing would mean writing scroll handling by hand for no
/// visual gain on what is a plain data grid.
///
/// A row is highlighted for a moment when its value changes, which is what separates a
/// register the firmware actively writes from one stuck at its init value.
/// </summary>
public sealed class RegisterTable : ListView
{
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(1);

    private readonly ushort[] _previous = new ushort[HvProtocol.MaxRegisterIndexExclusive];
    private readonly bool[] _seen = new bool[HvProtocol.MaxRegisterIndexExclusive];
    private readonly long[] _changedAt = new long[HvProtocol.MaxRegisterIndexExclusive];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public RegisterTable()
    {
        View = View.Details;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = true;
        BorderStyle = BorderStyle.None;
        GridLines = false;
        TabStop = false;
        BackColor = Theme.Card;
        ForeColor = Theme.InkSecondary;
        Font = new Font(Theme.FamilyName, 9f);

        Columns.Add("idx", 46, HorizontalAlignment.Right);
        Columns.Add("Name", 220);
        Columns.Add("Raw", 120, HorizontalAlignment.Right);
        Columns.Add("Value", 120, HorizontalAlignment.Right);
        Columns.Add("Note", 320);

        foreach (var d in RegisterCatalog.All)
        {
            var item = new ListViewItem(d.Index.ToString()) { Tag = d.Index };
            item.SubItems.Add(d.IsKnown ? d.Name : "—");
            item.SubItems.Add("—");
            item.SubItems.Add("—");
            item.SubItems.Add("");
            Items.Add(item);
        }

        // Display-only: a selection highlight would suggest the rows are actionable
        ItemSelectionChanged += (_, _) => SelectedIndices.Clear();
    }

    public void UpdateData(BmsSnapshot? snapshot)
    {
        BeginUpdate();
        try
        {
            long now = _clock.ElapsedMilliseconds;
            foreach (ListViewItem item in Items)
            {
                byte index = (byte)item.Tag!;
                bool valid = snapshot is not null && snapshot.RegisterValid[index];

                if (!valid)
                {
                    item.SubItems[2].Text = "—";
                    item.SubItems[3].Text = "—";
                    item.SubItems[4].Text = snapshot is null ? "" : "no answer";
                    item.ForeColor = Theme.InkMuted;
                    continue;
                }

                ushort raw = snapshot!.Registers[index];
                if (!_seen[index] || _previous[index] != raw)
                {
                    if (_seen[index]) _changedAt[index] = now;
                    _previous[index] = raw;
                    _seen[index] = true;
                }

                item.SubItems[2].Text = RegisterCatalog.FormatRaw(index, raw);
                item.SubItems[3].Text = RegisterCatalog.FormatValue(index, raw);
                item.SubItems[4].Text = RegisterCatalog.FormatNote(index, raw);

                bool fresh = _changedAt[index] != 0 &&
                             now - _changedAt[index] < HighlightFor.TotalMilliseconds;
                item.ForeColor = fresh ? Theme.Warning : Theme.Ink;
            }
        }
        finally
        {
            EndUpdate();
        }
    }
}
```

- [ ] **Step 2: Add the tab** — `BmsUi/Form1.Designer.cs`

Declare the fields next to the other tab members:

```csharp
    private TabPage registersTab;
    private RegisterTable registersTable;
    private Label registersNoteLabel;
```

Build them in `InitializeComponent`, just before the log tab section:

```csharp
        // ---------------- registers tab ----------------
        registersTable = new RegisterTable { Dock = DockStyle.Fill };
        registersNoteLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(6, 4, 6, 0),
            Text = "Read-only. Swept at 1 Hz only while this tab is visible. A row highlights " +
                   "when its value changes — a register stuck at its init value stays dim.",
        };
        registersTab = new TabPage("Registers");
        registersTab.Controls.Add(registersTable);
        registersTab.Controls.Add(registersNoteLabel);
```

Add it to the tab list:

```csharp
        tabs.TabPages.AddRange(new[]
        {
            voltageTab, temperatureTab, balanceTab, registersTab, settingsTab, logTab,
        });
```

Wire the selection event right after `tabs` is created:

```csharp
        tabs.SelectedIndexChanged += tabs_SelectedIndexChanged;
```

- [ ] **Step 3: Wire it up** — `BmsUi/Form1.cs`

Add the handler next to the other UI update methods:

```csharp
    /// <summary>
    /// The register sweep is 33 extra transactions per second, so it only runs while the
    /// Registers tab is actually on screen.
    /// </summary>
    private void tabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_worker is not null) _worker.IncludeAllRegisters = tabs.SelectedTab == registersTab;
    }
```

In `startButton_Click`, right after `_worker.Start();`:

```csharp
        _worker.IncludeAllRegisters = tabs.SelectedTab == registersTab;
```

In `ApplySnapshot`, next to the other view updates:

```csharp
        registersTable.UpdateData(s);
```

In `UpdateDashboard`, so the table clears on disconnect — add as the last line:

```csharp
        if (snapshot is null) registersTable.UpdateData(null);
```

Expose it for tests, next to the other internal accessors:

```csharp
    internal RegisterTable RegistersTable => registersTable;
    internal TabPage RegistersTab => registersTab;
```

- [ ] **Step 4: Give the simulator plausible charger values** — `BmsUi/Serial/SimulatedTransport.cs`

In the constructor, after the existing `_registers[...]` assignments:

```csharp
        // So the Registers tab is not a wall of zeros in simulation. Scales follow the
        // firmware: voltages x100, currents x10.
        _registers[2] = 2;        // CHARGING_STATE = CHARGING
        _registers[3] = 39000;    // CHARGER_SET_VOLTAGE  390.00 V
        _registers[4] = 60;       // CHARGER_SET_CURRENT    6.0 A
        _registers[31] = unchecked((ushort)(short)-60);   // CHARGE_CURRENT -6.0 A
        _registers[34] = unchecked((ushort)(short)-500);  // CHARGE_OVER_CURRENT_TRESHOLD
        _registers[35] = 3500;    // DISCHARGE_OVER_CURRENT_TRESHOLD
        for (byte i = 36; i <= 40; i++) _registers[i] = 100;   // error delays, ms
```

And in `Advance()`, after the other register updates, so the charger rows move:

```csharp
        // Charger tracks the pack while "charging"
        _registers[5] = (ushort)(packV * 100);                      // CHARGER_ACTUAL_VOLTAGE
        _registers[6] = (ushort)(58 + 4 * Math.Sin(t / 5.0));       // CHARGER_ACTUAL_CURRENT
```

- [ ] **Step 5: Add the UI test** — append to `BmsUi.Tests/UiSmokeTests.cs`

```csharp
    /// <summary>
    /// The sweep is opt-in, so the table only fills when its tab is selected. This drives the
    /// whole path: tab selection -> worker flag -> sweep -> snapshot -> table.
    /// </summary>
    [Fact]
    public void RegistersTab_FillsWhileVisible()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Show();
            Application.DoEvents();

            form.TabsControl.SelectedTab = form.RegistersTab;
            form.SimulationCheckBox.Checked = true;
            form.StartStopButton.PerformClick();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string ChargerVoltageCell() => form.RegistersTable.Items
                .Cast<ListViewItem>()
                .First(i => (byte)i.Tag! == 5)
                .SubItems[3].Text;

            while (sw.ElapsedMilliseconds < 8000 && ChargerVoltageCell() == "—")
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            Assert.EndsWith("V", ChargerVoltageCell());
            Assert.Equal(RegisterCatalog.All.Count, form.RegistersTable.Items.Count);

            form.StartStopButton.PerformClick();
            form.Close();
        });
    }
```

Add `using BmsUi.Protocol;` to the file if it is not already there.

Also update the tab-count assertion in `MainForm_OpensAndLaysOutWithoutError`:

```csharp
            // Voltage / Temperature / Balance / Registers / Settings / Log
            Assert.Equal(6, form.TabsControl.TabPages.Count);
```

- [ ] **Step 6: Build and run everything**

Run: `dotnet build -c Release --no-incremental` then `dotnet test`
Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 7: Look at it**

Run the render test and open the PNG:

```bash
dotnet test --filter FullWindow_RendersWithLiveSimulationData
```

Then inspect `%TEMP%\bmsui_preview_window.png`. Check the Registers tab is present, columns
line up and the charger rows carry values.

- [ ] **Step 8: Commit**

```bash
git add BmsUi/Ui/RegisterTable.cs BmsUi/Form1.cs BmsUi/Form1.Designer.cs BmsUi/Serial/SimulatedTransport.cs BmsUi.Tests/UiSmokeTests.cs
git commit -m "feat: Registers tab listing every readable MAINBUFFER index"
```

---

### Task 4: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/design-decisions.md`

- [ ] **Step 1: Document the tab in `README.md`**

Add `Registers` to the tab list, and a section after "Log tab (CSV)":

```markdown
## Registers tab

A read-only table of every readable MAINBUFFER index (0-40 and 44-49; 41/42/43 are shadowed by
the `0x29`/`0x2A`/`0x2B` commands and cannot be read). Each row shows the raw value, the scaled
value with its unit, and a note — decoded bits for FAULTS and OUTPUTS, the enum name for
CHARGING_STATE.

Unnamed indices are listed too, because "is the firmware writing anything to idx 19?" is
exactly what this view is for. A row highlights briefly when its value changes, so a register
the firmware actively writes looks different from one stuck at its init value.

The sweep costs 33 transactions per second and runs **only while this tab is visible**.
```

- [ ] **Step 2: Record the firmware findings in `docs/design-decisions.md`**

Add to the "Things that bit us" section:

```markdown
- **`CHARGING_STATE` (idx 2) always reads `NO_CHARGER`.** The firmware writes the register at
  `main.cpp:1586`, before the local `CHARGER` variable is assigned at `main.cpp:1665`, and
  never writes it again. Moving the write to the end of `ChargeControl_Task` fixes it.
- **`VCUflag` latches and is never cleared** (`main.cpp:509`). One VCU frame on FDCAN2 disables
  `ChargeControl_Task` until reset, so the charger registers stop updating even after the VCU
  is unplugged. A timeout on the last VCU frame would make the mode selection recover on its
  own.
- **`OPEN_CIRCUIT_VOLTAGE` (18) and `POWER` (41) are never written** by the firmware. Index 41
  is unreadable anyway, being shadowed by the voltage command.
```

- [ ] **Step 3: Commit**

```bash
git add README.md docs/design-decisions.md
git commit -m "docs: Registers tab and the firmware findings behind it"
```

---

## Self-review notes

- **Spec coverage:** columns and groups → Task 1 and 3; catalog → Task 1; polling strategy and
  the "skip already-polled indices" rule → Task 2; change highlighting and "no answer" → Task 3
  (`RegisterTable.UpdateData`); ListView justification → Task 3; simulator values → Task 3;
  read-only → enforced by using `ReadRegister` only; every test listed in the spec has a step.
- **Deliberate omission:** the spec's "out of scope" items (writing registers, refactoring
  `BmsSnapshot`, a charging dashboard, logging registers to CSV) have no tasks, by design.
- **Naming consistency:** `RegisterCatalog.All/Describe/FormatRaw/FormatValue/FormatNote`,
  `PollSchedule.SweepRegisters`, `PollItem.AllRegisters`, `PollWorker.IncludeAllRegisters`,
  `RegisterTable.UpdateData` are used identically in every task that references them.
