# BMS UI — Formula Student HV BMS Desktop Interface

Windows application that connects to the USB CDC interface of the HV battery master board
(STM32G474 + FreeRTOS) and shows the voltage/temperature of all 96 cells plus pack data,
fault state, balancing state and contactor outputs.

It is the HV counterpart of `lvbmsgui` (LV BMS): same stack (C# .NET 10 WinForms +
`System.IO.Ports`), different protocol — LV pushed ASCII lines, HV speaks a **binary
request/response** protocol.

## Requirements

- Windows
- .NET 10 SDK to build — the .NET 10 Desktop Runtime is enough to run
- Running without hardware needs **nothing extra**: there is a built-in simulation mode
  (see below). The external `bms_simulator.py` is only needed if you also want to exercise
  a real serial port (Python 3 + `pyserial` + com0com/VSPE with `COM10 ↔ COM11`).

## Build and run

```bash
dotnet build -c Release
dotnet run --project BmsUi
dotnet test
```

## Usage

1. Pick the COM port from the list and press **Start** (with no board, tick **Simulation**
   and press Start). The list **refreshes itself** when a board is plugged or unplugged, and
   each port is labelled with its kind (`COM12 — USB`, `COM3 — Bluetooth`, `COM5 — ST-Link`)
   so it is obvious which one is the BMS.
2. The app first sends the `0x17 0x71` ping; if the device does not echo it back, it refuses
   to connect (this prevents attaching to the wrong port and showing nonsense).
3. Once connected the poll worker starts and the left panel plus the tabs update live.

**Left panel** (visible on every tab), top to bottom:

| Section | Contents |
|---|---|
| PACK | Pack voltage in large type, plus current / power / SoC / max slave temperature tiles |
| CELLS | Min-max-average voltage and temperature, which cell (`#42`), plus **spread (max-min)** and **standard deviation** in mV — the numbers to read at a glance for imbalance |
| OUTPUTS | AIR / PRE / ERR status pills |
| FAULTS | Only **active** faults are listed; a green "No active faults" when there are none |
| (footer) | CRC / timeout / id counters, data age, link state |

The fault panel deliberately shows active faults only: a static 15-row list filled half the
screen and made a real fault harder to spot.

**Tabs:** Voltage · Temperature · Balance · Registers · Settings · Log

> The application **never writes to the BMS** — it only reads. A threshold/config write
> interface was deliberately left out; the Settings tab only changes how the UI looks.

## Cell rendering

Each cell is drawn as a **vertical battery silhouette** (body plus a terminal on top); the
font size scales with the window. Body and terminal are built as a single path so the alarm
outline follows the silhouette rather than the inner seam where they meet.

| Element | Meaning |
|---|---|
| Fill colour | **Voltage:** low = red, mid = yellow, high = green. **Temperature:** low = dark, high = bright amber (green is not used, since a high temperature is not a good thing) |
| Number in the middle | The value itself — colour is approximate, the number is exact |
| Amber outline + **⚠** (top right) | Value is outside the alarm thresholds |
| **▲ / ▼** (top right) | Above / below the overall mean of the 96 cells |
| **σ+ / σ−** (bottom right) | More than ±1σ from its own segment's mean |
| **B** badge (bottom left) | Cell is balancing |
| Grey fill + "—" | Cell is invalid/stale (0.00 V) |
| Number at top left | Linear cell index — the same numbering as the min/max indices in the left panel |

Colour never carries information on its own: the value is printed on every cell and cells
outside the thresholds also get an icon. That keeps the display readable for red-green colour
blindness (the most common type). The balance badge needs its outline: gold drops to almost
the same tone as the fill in the yellow-orange middle of the ramp and disappeared without it.

**An alarm does not change the fill.** The low end of the voltage scale is already red, so a
red alarm fill would be confused with a "low but normal" cell; worse, a badly set threshold
would flatten the whole grid to a single colour and no cell could be told apart from another.
The alarm is shown as an **amber outline + ⚠** instead (same colour as the icon, with a dark
casing underneath so it stays visible on an amber fill), and the fill keeps showing the value.

If an unexpectedly large number of cells are outlined, the alarm threshold is too tight —
check the Settings tab; "Restore defaults" puts the firmware thresholds back.

**Why are the two statistical marks separate?** ▲/▼ gives a cell's position relative to the
whole pack, while σ± says whether it is drifting away from its own neighbours. If a segment
sits entirely below the pack mean, that segment's highest cell can be "σ+" and still be "▼" —
they answer different questions. Segment sigma is computed over all 16 cells of the segment
(population). The pack-wide standard deviation is shown in the left panel in mV.

## Settings tab (view only)

Separately for voltage and temperature:

- **Alarm low/high threshold** — a cell outside them gets an amber outline and a warning icon
- **Colour scale low/high end** — the two ends of the heatmap. When the pack runs in a narrow
  band (e.g. 3.87-4.02 V), narrowing the scale makes the differences between cells visible.

Defaults match the firmware thresholds (2.50 / 4.23 V, 80 °C — `main.h:194-200`), so out of
the box a UI alarm lines up with a BMS fault. Settings are stored in
`%APPDATA%\BmsUi\settings.json` and restored on the next start. **None of these values is
ever sent to the device.**

## Log tab (CSV)

Choose a file and press **Start recording**; the default rate is 1 Hz, adjustable between 0.1
and 10 Hz. Rows are appended to an existing file and the header is not repeated.

Column names follow the team's **SD-card template** (`CAN Hattı ve SDCARD.xlsx` → SDCARD
sheet), so the SD-card logs and this CSV can be processed with the same tooling:

```
TIMESTAMP,
BMS_CELL0_VOLTAGE_f … BMS_CELL95_VOLTAGE_f,
BMS_CELL0_TEMP_f … BMS_CELL95_TEMP_f,
BMS_BALANCE_IC0_u16 … BMS_BALANCE_IC5_u16,
BMS_TOTAL_VOLTAGE_f, BMS_TOTAL_CELL_VOLTAGE_f, BMS_CURRENT_f, BMS_POWER_f,
BMS_ESTIMATED_SoC_f, BMS_FAULTS_u16, BMS_CONTRACTORS_u8,
BMS_MIN_CELL_VOLTAGE_f, BMS_MAX_CELL_VOLTAGE_f, BMS_AVG_CELL_VOLTAGE_f,
BMS_CELL_VOLTAGE_STDDEV_f, BMS_MIN_CELL_NUMBER_u8, BMS_MAX_CELL_NUMBER_u8,
BMS_MIN_CELL_TEMP_f, BMS_MAX_CELL_TEMP_f, BMS_AVG_CELL_TEMP_f, BMS_MAX_SLAVE_TEMP_f
```

216 columns. Units: voltage **V**, temperature **°C**, current **A**, power **W**, SoC **%**,
`FAULTS`/`CONTRACTORS` bit masks, balance 16 bits per IC.

`BMS_TOTAL_VOLTAGE_f` is the pack voltage register (idx 7) while `BMS_TOTAL_CELL_VOLTAGE_f`
is the firmware's sum of the 96 cells (idx 11) — the small difference between them comes from
the per-cell truncation and is a handy way to sanity-check the scaling.

Numbers are written with `InvariantCulture` (decimal **point**), so the file survives being
processed under a Turkish locale where the comma would break it. The on-screen display still
follows the local settings.

## Registers tab

A read-only table of every readable MAINBUFFER index (0-40 and 44-49; 41/42/43 are shadowed by
the `0x29`/`0x2A`/`0x2B` commands and cannot be read). Each row shows the raw value, the scaled
value with its unit, and a note — decoded bits for FAULTS and OUTPUTS, the enum name for
CHARGING_STATE.

Unnamed indices are listed too, because "is the firmware writing anything to idx 19?" is
exactly what this view is for. A row highlights briefly when its value changes, so a register
the firmware actively writes looks different from one stuck at its init value.

This is also where the charger data lives: the firmware fills `CHARGER_ACTUAL_VOLTAGE` and
`CHARGER_ACTUAL_CURRENT` from the charger's CAN frames, and nothing else in the UI reads them.

The sweep costs 33 transactions per second and runs **only while this tab is visible** — the
indices the regular schedule already polls at 5-10 Hz are not asked for again.

## Built-in simulation (no board needed)

Tick **Simulation** in the connection bar and press **Start**. No driver, no virtual COM port
and no Python; stop it whenever you like.

The simulation is a virtual device implementing `ISerialTransport`, the same interface a real
port implements ([SimulatedTransport.cs](BmsUi/Serial/SimulatedTransport.cs)). Data flows
through **exactly the same code path** as a real board — command dispatch, CRC8 verification,
frame parsing, poll worker — only the source of the bytes changes. What it produces:

- 96 cell voltages drifting between 3.30-4.19 V (one clear min cell, one clear max cell)
- Temperatures drifting with 3 hot cells; the firmware's `94 → 20` remap is mirrored too
- Current oscillating between -120 and +80 A, SoC derived from the mean voltage
- PRE closes at 2 s, AIR at 5 s
- So the fault panel is not always empty, every 12 s it rotates through cell OV / cell
  overtemperature / precharge timeout (the ERR light follows along)

## External simulator (to exercise a real serial port)

```bash
python bms_simulator.py --port COM11
```

Then select `COM10` in the application and press Start.

| Flag | Effect |
|---|---|
| `--port COM11` | Port the simulator holds |
| `--fault 2 --fault 13` | Activates the given FAULTS bits |
| `--chunked` | Splits responses into 64-byte chunks (exercises host reassembly) |
| `--latency 5` | Adds a delay before responding (ms) |
| `--verbose` | Prints every incoming packet |

The simulator behaves like the firmware: it dispatches on the **length** of the incoming byte
burst and appends a correct CRC8 to every response.

## Protocol summary

| Send | Meaning | Response |
|---|---|---|
| `0x29` (1 byte) | 96 cell voltages | 194 bytes: 96×uint16 LE, `[192]=0x29`, `[193]=CRC8` |
| `0x2A` (1 byte) | 96 cell temperatures | 194 bytes: 96×**int16** LE (signed), `[192]=0x2A`, `[193]=CRC8` |
| `0x2B` (1 byte) | Balance state | 14 bytes: 6×uint16 LE dcc bitmap, `[12]=0x2B`, `[13]=CRC8` |
| `idx` (idx<50, 1 byte) | Read `MAINBUFFER[idx]` | 4 bytes: uint16 LE, `[2]=idx`, `[3]=CRC8` |
| `0x17 0x71` (2 bytes) | Ping | 2-byte echo (no CRC) |
| `idx,valLSB,valMSB` (3 bytes) | Write `MAINBUFFER[idx]=val` | 4 bytes (same shape as a read) |

- Cell order: linear `0..95 = segment*16 + cell` (6 segments × 16 cells).
- Voltage `raw/100` V · temperature `(int16)raw/100` °C · `PACK_CURRENT` signed `raw/10` A ·
  SoC `raw/10000`.
- CRC8 = CRC-8/SMBUS (poly `0x07`, init `0x00`, no reflection) over every byte except the last.

### MAINBUFFER indices

| idx | Name | Scale |
|---|---|---|
| 0 | FAULTS | bit mask |
| 1 | OUTPUTS | bit0=AIR, bit1=PRE, bit2=ERR/SDC |
| 7 | PACK_VOLTAGE | ×100 V |
| 8 | PACK_CURRENT | signed ×10 A |
| 9 / 10 | MAX / MIN_CELL_VOLTAGE | ×100 V |
| 11 | TOTAL_CELL_VOLTAGE | ×100 V |
| 12 / 13 | MAX / MIN_CELL_TEMP | signed ×100 °C |
| 14 / 15 | AVG_CELL_VOLTAGE / _TEMP | ×100 |
| 16 | MAX_SLAVE_TEMP | signed ×100 |
| 17 | ESTIMATED_SoC | ×10000 |
| 30 | ALLOWED_DISBALANCE | mV (writable) |
| 32 / 33 | PRECHARGE_PERCENTAGE / _TIMEOUT | writable |

### FAULTS bits

0 PEC/comms · 1 cell UV · 2 cell OV · 3 discharge overcurrent · 4 charge overcurrent ·
5 cell undertemperature · 6 cell overtemperature · 7 cell open wire · 8 no current sensor ·
9 slave overtemperature · 10 pack UV · 11 pack OV · 12 temperature open wire ·
13 precharge timeout · 14 measurement stale

## Architecture

```
Form1  ──Invoke──  PollWorker (background thread, 10 Hz)
                        │
                   SerialLink  (request/response, CRC + id verification, error counters)
                        │
                   ISerialTransport ──> SerialPortTransport (System.IO.Ports)
                                   ├──> SimulatedTransport  (in-app simulation)
                                   └──> FakeTransport / FakeDeviceTransport (tests)
```

Poll schedule (10 Hz base tick, ~97 transactions/s):

| Data | Rate |
|---|---|
| FAULTS, OUTPUTS, PACK_VOLTAGE, PACK_CURRENT | 10 Hz |
| min/max/avg/total/slave/SoC (idx 9-17) | 5 Hz |
| 96 voltages + 96 temperatures | 5 Hz |
| Balance | 2 Hz |

Only the worker thread touches the port; write requests coming from the UI are queued and
executed between poll rounds.

## Known limitations

- **Balancing cannot be switched on or off.** In the firmware `BalanceEnable` is not part of
  MAINBUFFER but a separate `volatile` global, and command `0x2B` only reads state. Adding UI
  control would require a new USB command in the firmware.
- **Power (kW) is computed host-side** (`V × I`). The firmware has `MAINBUFFER[41]=POWER`, but
  index 41 is shadowed by the `0x29` voltage command and cannot be read over USB. The same
  applies to indices 42 and 43.
- **Cell 94 mirrors cell 20.** The firmware does `cellTemps[94] = cellTemps[20]`
  (`main.cpp:971`), so cell 94's temperature reads the same as cell 20's.
- **SoC.** During planning the firmware did not compute `ESTIMATED_SoC` (it was fixed at 0).
  A test with the board read back a non-zero value, but the firmware was producing
  **simulated data** at the time — whether it is genuinely computed still needs to be
  confirmed on the firmware side.
- Sending `idx >= 50` gets **no response at all** from the device, which is why every
  transaction has a timeout.
- Commands are sent one at a time. Because the firmware dispatches on packet **length**, two
  commands merged into one USB packet would be read as a ping — hence the
  one-transaction-at-a-time rule.

> **When testing with the board:** if the firmware is in simulation mode, the cell values on
> screen do not reflect real cells. Protocol verification (CRC/timeout counters, scaling
> cross-check) still holds — that is independent of the data — but observations about
> imbalance, sigma marks or hot cells are only meaningful with real cells.

## Tests

`dotnet test` — 151 tests:

- CRC-8/SMBUS against known vectors (`"123456789"` → `0xF4`)
- 194/14/4-byte frame parsing, signed temperature and current, rejection of a corrupt CRC and
  a wrong frame id
- **Python ↔ C# cross-check**: real bytes produced by `bms_simulator.py` are pinned as
  constants and decoded by the C# parser
- Reassembly of a chunked 194-byte response, timeout behaviour, error counters
- PollWorker end to end: fake device → SerialLink → parser → snapshot, link loss, register write
- Statistics: population standard deviation, per-segment sigma, and the case where the segment
  mark and the pack mean point in opposite directions
- Colour: the ink chosen for every ramp step clears the 4.5:1 WCAG AA threshold; an alarm is
  not a ramp step; user thresholds override the firmware defaults
- Display settings round-trip to disk, fall back to defaults on a corrupt file, repair
  inverted ranges
- UI smoke tests: does the main window open and lay out, does the grid draw the alarm /
  invalid / balancing states, is the logo still usable in a second window
- Register catalog: names, scales and signedness; formatting asserted against
  InvariantCulture so the tests do not depend on the machine's language
- Register sweep: covers every readable index the regular schedule misses, never a shadowed
  one, and costs nothing while the tab is closed
- Rendering tests leave PNGs behind (`%TEMP%\bmsui_preview_*.png`) — grid, whole window and
  the Settings tab, handy for a visual check
