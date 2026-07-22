# Register Inspector — Design

Date: 2026-07-22
Status: Approved, ready to plan

## Purpose

Show every readable MAINBUFFER register live, in one table, with its name, raw value and
scaled value. The application currently surfaces only the 14 registers it needs; everything
else the firmware maintains is invisible.

The motivating case is charging. The firmware fills `CHARGER_ACTUAL_VOLTAGE` /
`CHARGER_ACTUAL_CURRENT` from the charger's CAN frames, but nothing in the UI reads them. A
charging-specific dashboard was rejected: the team's charger system is being replaced this
season, so a view built around today's charging semantics would be thrown away. A register
inspector serves the same need and survives the change — when the charger system changes, the
registers change meaning, not the tool.

The second motivation is diagnostic. Three questions came up while designing this and all
three had to be answered by reading firmware source:

- `CHARGING_STATE` (idx 2) always reads `NO_CHARGER`. The write at `main.cpp:1586` happens
  before the local `CHARGER` variable is assigned at `main.cpp:1665`, and there is no second
  write.
- `OPEN_CIRCUIT_VOLTAGE` (idx 18) and `POWER` (idx 41) are never written by the firmware.
- The charger registers only update when `ChargeControl_Task` runs, which it refuses to do
  once `VCUflag` latches (`main.cpp:1589`).

A live table showing "this value never changes" or "this index never answers" makes all three
visible without opening the firmware.

## What it shows

All readable indices: **0-40 and 44-49** (47 rows). Indices 41/42/43 are shadowed by the
`0x29`/`0x2A`/`0x2B` commands and can never be read; indices ≥ 50 are dropped by the firmware.

Unnamed indices are listed too, by index. That is deliberate: "is the firmware writing
anything to idx 19?" is exactly the kind of question this view exists to answer.

### Columns

| Column | Contents |
|---|---|
| `idx` | Register index |
| Name | From the catalog, or `—` when unknown |
| Raw | Decimal, plus hex for bit-mask registers |
| Value | Scaled and unit-suffixed (`374.41 V`, `-37.5 A`, `73.00 %`) |
| Note | Decoded bits for FAULTS/OUTPUTS; `no answer` when the register never responded |

### Groups

Rows are grouped, in this order:

- **Status** — 0 FAULTS, 1 OUTPUTS
- **Charger** — 2 CHARGING_STATE, 3/4 SET_VOLTAGE/CURRENT, 5/6 ACTUAL_VOLTAGE/CURRENT,
  31 CHARGE_CURRENT
- **Pack** — 7-18
- **Thresholds / config** — 30, 32-40
- **Unnamed** — 19-29, 44-49

## Register catalog

Scale and sign information currently lives implicitly inside `BmsSnapshot` properties
(`(short)Registers[Reg.PackCurrent] / 10.0` and friends). The inspector needs the same
knowledge as data, so a new `RegisterCatalog` holds it:

```csharp
public readonly record struct RegisterDescriptor(
    byte Index, string Name, string Unit, double Scale, bool Signed, RegisterGroup Group);
```

- `Scale` is the divisor: `PACK_VOLTAGE` → 100, `PACK_CURRENT` → 10, `ESTIMATED_SoC` → 100
  (raw/10000 expressed as a percentage).
- `Signed` selects `(short)` reinterpretation before scaling — this is what makes negative
  current and negative temperature read correctly.
- Registers with no meaningful scale (bit masks, plain counts) carry `Scale = 1` and an empty
  unit.

The catalog is pure data plus a formatter, so it is unit-testable without any UI.

`BmsSnapshot` is **not** refactored to use the catalog in this change. That refactor is
tempting but touches working, tested code for no functional gain; it can follow later if the
duplication actually starts to hurt.

## Polling

Reading 47 registers continuously would add ~47 transactions/s on top of the current ~97/s.
Instead the extra sweep runs **only while the Registers tab is visible**, at 1 Hz.

- `PollSchedule` gains an `AllRegisters` item, scheduled every 10th tick.
- `PollWorker` gains a `bool IncludeAllRegisters` property, read at the start of each tick.
- `Form1` sets it from `tabs.SelectedIndexChanged`.

The sweep covers only the indices **not** already polled on the regular schedule. Fast
registers (0, 1, 7, 8), summary registers (9-17) and the config register (30) are refreshed at
5-10 Hz anyway, and their values are already in the snapshot the table reads from — asking for
them again would be redundant traffic for staler data. That leaves 33 indices in the sweep:
2-6, 18-29, 31-40, 44-49.

Cost while the tab is closed: zero. Cost while open: 33 transactions/s, well inside the
existing budget.

Values land in `BmsSnapshot.Registers` / `RegisterValid`, which already exist and are already
sized for all 50 indices. No new model type is needed.

## Change highlighting

When a register's value differs from the previous snapshot, its row is highlighted briefly
(about one second). This is what turns the table from a data dump into a diagnostic: a
register stuck at its init value looks obviously different from one the firmware is actively
writing.

Indices that have never answered show `—` and the note `no answer`.

## UI

47 rows do not fit a typical window height, so the view needs scrolling. A `ListView` in
`Details` mode provides that for free; owner-drawing the table would mean writing scroll
handling by hand for no visual gain on what is a plain data grid.

It is styled to match the dark theme and selection is disabled, the same treatment the fault
list got.

The view is **read-only**. The application does not write to the BMS, and this change does not
alter that.

## Simulator

`SimulatedTransport` currently answers unknown indices with zero, which would make the table
look dead in simulation. It gains plausible values for the charger group so the tab can be
developed and demonstrated without hardware.

## Testing

- **Catalog**: names, scales, signedness; unknown indices produce a well-formed row; signed
  registers format negative values correctly.
- **Pollable set**: the sweep never includes 41/42/43 and never an index ≥ 50 — a violation
  would throw at runtime inside `SerialLink`.
- **Formatting**: bit-mask registers show decoded bits; a register that never answered renders
  as `no answer` rather than `0`.
- **UI smoke**: with simulation running and the Registers tab selected, the table fills and the
  charger rows show non-zero values.

## Out of scope

- Writing registers from the UI.
- Refactoring `BmsSnapshot` onto the catalog.
- A charging-specific dashboard. Once the new charger system is in place and its registers are
  stable, that can be built on top of this.
- Logging the full register set to CSV. The CSV schema mirrors the team's SD-card template and
  should not diverge from it casually.
