# BMS UI — Design Decisions

Why the code looks the way it does. The README describes *what* the application does; this
document records the decisions and the constraints behind them, so they are not accidentally
undone later.

Firmware source of truth:
`workspace_1.19.0/bms_master_baremetal_freertos-claude-bms-freertos-migration-elqosd`
— `Core/Src/main.cpp` (`USB_Task`, `USBTransmit2bytes`, `calculateCRC8`),
`Core/Inc/main.h` (MAINBUFFER indices, thresholds).

## 1. Protocol constraints that shape the code

Three properties of the firmware drive most of the transport design.

**Dispatch is by packet length** (`main.cpp:1958`, `switch (usblen)`). `usblen` is the number
of bytes delivered in a single CDC RX callback. If the host wrote two 1-byte commands in quick
succession and they merged into one USB packet, the device would read that as the 2-byte ping.
→ **One transaction at a time**: a command goes out in a single `Write`, and no new command is
sent before the previous response has been read in full.

**Indices 41/42/43 are shadowed.** `main.h:123` defines `POWER 41`, but for 1-byte packets the
device intercepts 41/42/43 as the voltage/temperature/balance commands, so those MAINBUFFER
entries can never be read.
→ Power is computed on the host as `V × I`.

**`idx >= 50` is dropped silently** (`main.cpp:1998`) — no response at all.
→ Every transaction needs a timeout; otherwise a single bad index hangs the worker thread.

Everything else follows from the frame layout, which the README documents.

## 2. Architecture

```
Form1 ──Invoke── PollWorker ── SerialLink ── ISerialTransport ── SerialPortTransport
                                                              ├─ SimulatedTransport
                                                              └─ Fake*Transport (tests)
```

**`ISerialTransport` exists for testability.** Building `SerialLink` on an interface rather
than `SerialPort` directly is what makes chunked delivery, timeouts and error counters
testable without a COM port — and it is also what let the in-app simulation reuse the entire
stack instead of becoming a parallel code path.

**Only the worker thread touches the port.** Write requests from the UI are queued and
executed between poll rounds, so two threads never write at once.

**Tiered polling** (10 Hz base tick, ~97 transactions/s): fast registers every tick, cell
arrays and summaries every second tick, balance every fifth. The scheduling decision lives in
`PollSchedule` as a pure function so it can be tested; `PollWorker` is the thin wrapper around
it.

**Snapshots always carry full state.** Fields not refreshed on a given tick keep their previous
values, and each group has its own timestamp — that is where the "data age" readout comes from.

- **`EventLog`** is a pure diff over the FAULTS and OUTPUTS registers, the same shape as
  `PollSchedule` and `CellAnalysis`: the timestamp is injected, so fault durations are
  deterministic under test. The one impurity — `DateTime.Now` — stays at the call site in
  `Form1`. A disconnect re-baselines it so a gap in the data never reads as every fault
  clearing at once.

## 3. What the UI does not do

The application **only reads**. An earlier design had a config panel that wrote thresholds to
MAINBUFFER (indices 30/32/33); it was removed. The plumbing underneath
(`SerialLink.WriteRegister`, `PollWorker.EnqueueWrite`) is still there and still tested, so
the panel can come back without redesigning the transport.

`ALLOWED_DISBALANCE` (idx 30) is still *read*, because the balance summary displays it.

## 4. Visual decisions

These were reached iteratively, mostly by rendering the grid to a PNG and looking at it.

**Cells are drawn, not composed from controls.** 96 Labels do not scale; one owner-drawn
`Paint` does, and it keeps typography and alignment consistent. Same reasoning for the left
panel: a single `DashboardPanel` instead of dozens of GroupBoxes.

**The fill always encodes the value; alarms never take it over.** The low end of the voltage
ramp is red, so a red alarm fill would be indistinguishable from a "low but normal" cell.
Worse, a badly set threshold would flatten the entire grid to one colour. Alarms are drawn as
an amber outline plus a ⚠ icon, matching each other, with a dark casing so the outline stays
visible on an amber fill.

**Colour is never the only channel.** The value is printed on every cell, and threshold
breaches also carry an icon. Red-green is the riskiest pair for colour blindness, and the
voltage ramp deliberately runs red → yellow → green because that is what a BMS operator
expects to read — the redundancy is what makes that safe.

**Ink colour is chosen by real contrast, not by perceived brightness.** The obvious
`0.299R+0.587G+0.114B` heuristic with a fixed threshold picks white on saturated green
(#22C55E scores 0.535), where the actual contrast is 2.3 on white versus 8.6 on dark. Cells
drifting in and out of the top ramp step flipped their text colour. `Heatmap.InkOn` now
compares WCAG relative luminance for both options.

Related: the dark ink is **pure black**, not #0B0B0B. Any ramp that runs dark-to-light passes
through the point where white and dark ink tie, and the contrast ceiling there is set by how
dark the dark ink is. With #0B0B0B the floor was 4.44 — below the AA threshold of 4.5. A test
caught this on the amber ramp's `#AD6610` step.

**Two independent statistical marks.** ▲/▼ compares a cell to the mean of all 96; σ± compares
it to its own segment. They must stay separate: a segment sitting entirely below the pack mean
can contain a cell that is "σ+" *and* "▼". Segment sigma is a population statistic over all 16
cells of the segment.

**The fault panel lists only active faults.** A static 15-row list took half the panel and made
a real fault harder to notice.

## 5. Things that bit us

Recorded so they are not rediscovered the hard way.

- **`SplitterDistance` set in the designer is silently clamped** when the control has not been
  sized yet. It has to be applied in `OnLoad`. The symptom was a 130 px left panel with
  overlapping labels.
- **`PictureBox` disposes the `Image` assigned to it.** Handing every window the same static
  logo instance means the second window renders a red X. `Branding.CreateLogo()` returns a
  copy per call.
- **`bin/` and `obj/` were committed** despite being in `.gitignore` — git ignores the file
  only until it is tracked. 342 files were untracked afterwards.
- **Building while the app is running** can leave `bin/Release` half-populated. If
  `runtimeconfig.json` goes missing the app reports "You must install .NET Desktop Runtime",
  which sends you chasing a runtime problem that does not exist. Close the app, rebuild.
- **`GraphicsPath.AddArc` connects to the previous point with a straight line**, and that line
  is diagonal. The straight edges of the battery silhouette have to be added explicitly, or
  the terminal becomes a triangle.
- **A bare `Thread.Sleep` in a UI test starves the message pump.** `BeginInvoke` callbacks
  never run, so the view keeps rendering a stale snapshot and the screenshot looks like the
  feature is broken when it is not. Wait in a loop that calls `Application.DoEvents()`.
- **Selecting a tab by index in a test breaks the moment a tab is inserted.** Select by
  reference.
- **`ListView` is not double buffered** and `DoubleBuffered` is protected, so the flicker fix
  needs a subclass. Feeding it live data made the register table visibly strobe. Two things
  made it worse and are worth remembering separately: assigning a `SubItem.Text` invalidates
  the item even when the text is unchanged, and the view was fed at the UI update rate rather
  than the rate its data can actually change at.
- **A `TabPage` has 3 px of padding a docked child cannot cover**, and its background defaults
  to `SystemColors.Control`. Every tab in the application was drawing a white ring around its
  own content until the pages were given a themed `BackColor`.
- **`DrawToBitmap` on a child control does not compose the way the screen does.** Asking the
  `TabControl` or a `TabPage` to draw itself produced a clean image while the running window
  still showed the white ring, so a test written against those surfaces passed against the
  bug it was written for. Only the whole `Form` reproduces it — and that render includes the
  title bar, which is legitimately light, so the assertion has to be limited to a region.
  Any test that asserts on rendered pixels is worth running once against the unfixed code.
- **Scroll bars are drawn by the system**, so owner-drawing a `ListView` leaves a light grey
  bar against a dark table. `SetWindowTheme(handle, "DarkMode_Explorer", null)` reaches it.
  The theme name is undocumented and is a no-op on Windows versions that do not know it, so
  nothing may depend on it having worked.
- **A splash screen cannot live on the UI thread.** Startup was profiled before writing one:
  `new Form1()` costs ~426 ms and first paint another ~258 ms, all on the main thread — so a
  splash owned by that thread is frozen for precisely the window it exists to fill.
  `SplashScreen` runs its own STA thread with its own `Application.Run`, which also means the
  two overlap and the splash adds no startup time. Worth recording separately: the profile
  cleared the obvious suspect — `SerialPortCatalog.List()` is 9 ms, the cost is `Form1`'s own
  construction (three 96-cell grids, seven tabs, a 47-row table).
- **An always-on-top splash needs a self-destruct.** If the signal to close never arrives — a
  crash mid-startup, a callback that never fires — the window sits over the desktop for the
  whole session with no way to reach it. A 15 s timer inside the form keeps the worst case at
  "no splash" rather than "an unclosable window".

## 6. Firmware observations

Found while designing the register inspector, recorded here because they are easy to
rediscover the hard way and all three are firmware-side.

- **`CHARGING_STATE` (idx 2) always reads `NO_CHARGER`.** The firmware writes the register at
  `main.cpp:1586`, before the local `CHARGER` variable is assigned at `main.cpp:1665`, and
  never writes it again. Moving the write to the end of `ChargeControl_Task` fixes it. Until
  then the UI cannot use it to tell whether charging is in progress.
- **`VCUflag` latches and is never cleared** (`main.cpp:509`). A single VCU frame on FDCAN2
  disables `ChargeControl_Task` until reset, so the charger registers stop updating even after
  the VCU is unplugged. A timeout on the last VCU frame would let the mode selection recover
  on its own.
- **`OPEN_CIRCUIT_VOLTAGE` (18) and `POWER` (41) are never written** by the firmware. Index 41
  is unreadable anyway, being shadowed by the voltage command, which is why the UI computes
  power as `V × I` on the host.

## 7. Testing approach

The protocol layer is covered by unit tests, but two things carry more weight than the rest:

**Python ↔ C# cross-check.** Real bytes produced by `bms_simulator.py` are pinned as constants
in `SimulatorFrameTests` and decoded by the C# parser. Two implementations of the same
protocol drifting apart is the sneakiest failure mode here, and this catches it.

**Rendering tests.** The grid, the whole window and the Settings tab are rendered to bitmaps.
They catch drawing exceptions, and the PNGs they leave in `%TEMP%` are how most of the visual
problems above were found — the layout bug in particular passed every assertion.

`SerialPortTransport` itself is not covered: it is a thin wrapper whose behaviour only shows up
against a real port.
