# Fault & Event Timeline — Design

Date: 2026-07-23
Status: Approved, ready to plan

## Purpose

The application shows faults and contactor outputs as they are *right now*. The left panel
lists the faults currently active and the AIR/PRE/ERR state currently held. Nothing keeps a
history, so a fault that appears and clears between two glances is gone with no trace.

On a moving car that is exactly the fault worth seeing. A brief overvoltage under regen, a
precharge that times out once, an ERR that blinks and clears — each leaves the pack in a state
that has to be explained afterwards, and each is invisible the moment it ends.

The timeline records every FAULTS bit and every AIR/PRE/ERR transition with a timestamp, so
"what happened, and when" survives the event.

## Scope

Events come from two registers only: FAULTS (idx 0) and OUTPUTS (idx 1). Both are in the fast
poll set, read at 10 Hz regardless of which tab is on screen, so the timeline fills whether or
not anyone is watching it.

Connection up/down is deliberately **not** an event (decided during design). Threshold
crossings on individual cells are **not** events either; that belongs with alarm
notifications, a separate feature.

A note on resolution: a host-side latch sees the registers at 10 Hz, so it catches any
transition that persists across at least one 100 ms poll. A fault that rises and clears inside
a single 100 ms window is invisible to it — only the firmware's own `faultsLatched` global
could catch that, and it is not currently exposed over USB.

## What counts as an event

- **FaultRaised** — a FAULTS bit went 0 → 1. Label is the bit's name from `FaultBits.Names`
  ("Cell overvoltage"). Severity Critical.
- **FaultCleared** — a FAULTS bit went 1 → 0. Same label, plus the duration it was set.
  Severity Info.
- **OutputOn / OutputOff** — AIR, PRE or ERR changed. Labels "AIR closed" / "AIR opened",
  "PRE active" / "PRE off", "ERR raised" / "ERR cleared". ERR is Critical; AIR and PRE are
  Info.

## Data model

One value type, in `Model/PackEvent.cs`:

```csharp
public enum PackEventType { FaultRaised, FaultCleared, OutputOn, OutputOff }
public enum EventSeverity { Info, Critical }

public readonly record struct PackEvent(
    DateTime At, PackEventType Type, string Label, TimeSpan? Duration, EventSeverity Severity);
```

`Label` is built when the event is emitted, so the UI and the exporter render the same text
with no shared formatting logic. `Duration` is set only on `FaultCleared`.

## The EventLog core

`Model/EventLog.cs` is a pure state machine — no UI, no timers, no `DateTime.Now` inside it.
It holds:

- the previous FAULTS and OUTPUTS masks,
- the onset timestamp of each currently-set fault bit (15 slots),
- a ring buffer of emitted events (capacity 1000) and a dropped-count,
- a "have a baseline" flag.

Its surface:

```csharp
IReadOnlyList<PackEvent> Events { get; }
int DroppedCount { get; }
IReadOnlyList<PackEvent> Observe(ushort faults, ushort outputs, DateTime at);
void SetDisconnected();
void Clear();
```

`Observe` diffs the new masks against the previous ones, emits an event per changed fault bit
and per changed output bit (fault durations computed from the stored onsets), appends them to
the buffer, and returns just the newly-emitted ones so the caller can decide whether to
repaint. The timestamp is passed in, which is what makes durations deterministic under test.

The buffer is a ring: past 1000 events the oldest is dropped and `DroppedCount` grows. A fault
oscillating at the 10 Hz poll rate emits at most ~20 events/s, so 1000 covers ~50 s of
pathological flapping; the dropped count keeps the UI honest about it.

## Connection handling

Two rules keep the log from inventing events across a gap in the data:

- **Disconnect emits nothing.** When the link drops, `SetDisconnected()` is called. It does
  not diff, so a disconnect never looks like every fault clearing at once.
- **Every (re)connection re-baselines silently.** The first `Observe` after start, and the
  first after a `SetDisconnected`, records the masks as the new baseline and emits nothing.
  Only transitions seen between two consecutive connected samples become events.

The consequence is stated plainly: whatever changed while disconnected is not recovered. A
fault already active when the link comes up is not silently lost, though — the left panel
shows it immediately, and it reaches the timeline on its next transition.

## UI

A new **Events** tab, drawn the same way as the Registers tab: an owner-drawn `ListView` used
only for scrolling, painted in the dark theme with the dark scroll bar. Newest event on top.

Columns:

| Column | Contents |
|---|---|
| Time | `HH:mm:ss.fff` |
| Event | severity-coloured icon + label (`▲` raised, `▼` cleared, `●` output) |
| Duration | the fault's lifetime, on FaultCleared rows only |

Below the list: a **Clear** button, an **Export…** button, and a line reading
"N events · M older dropped".

The control renders from `EventLog.Events`. `Form1.ApplySnapshot` calls `Observe` on every
snapshot and repaints the timeline only when `Observe` returned something. The log runs on
every snapshot regardless of the selected tab — unlike the register sweep, it must never be
gated on visibility.

## Export

`Logging/EventCsvExporter.cs` writes the current events to CSV with a fixed header:

```
TIMESTAMP,EVENT,STATE,DURATION_MS,SEVERITY
```

`STATE` is raised/cleared/on/off; `DURATION_MS` is blank except on cleared rows. Numbers and
timestamps use InvariantCulture, matching `CsvLogger`, so a Turkish locale's comma separator
cannot corrupt the file. A save dialog chooses the path; export is manual and the timeline is
otherwise in-memory only.

## Simulator

No change. `SimulatedTransport` already rotates FAULTS every 12 s (overvoltage,
overtemperature, precharge timeout) and toggles PRE/AIR/ERR, so the timeline populates on its
own in simulation.

## Testing

- **EventLog** (the core, pure): steady state emits nothing; a rising bit emits one
  FaultRaised; a falling bit emits one FaultCleared whose duration equals the injected elapsed
  time; two bits changing in one sample emit two events; output transitions emit OutputOn/Off
  with the right severity; a `SetDisconnected` followed by an `Observe` with different masks
  emits nothing (silent re-baseline); the ring buffer drops the oldest past capacity and
  counts the drops.
- **Export**: a known event list produces the fixed header and the expected row, formatted
  under InvariantCulture.
- **UI smoke**: the Events tab paints with no `SystemColors.Control` anywhere in its area, and
  with the simulator running the fault rotation the list gains rows.

## Out of scope

- Writing anything to the BMS. The timeline is read-only, like the rest of the application.
- Connection up/down as events, and per-cell threshold crossings.
- Auto-persisting the log to disk as it runs. Export is manual; the raw FAULTS/CONTRACTORS
  columns are already in the snapshot CSV when logging is enabled.
- CHARGING_STATE transitions. The register always reads NO_CHARGER (a firmware ordering bug),
  so an event built on it would mislead.
