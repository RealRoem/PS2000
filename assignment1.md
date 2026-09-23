# Assignment 1 — PS2000B GUI

Status snapshot for whoever (human or AI) picks this up next. This assignment is done and
working against real hardware; what's left is polish and verification items listed at the
bottom.

## The task

> The architectural driver for this project is 'time to market'.
>
> Make a graphical user interface for the PS2000 that does the following:
> - at startup it checks the device type (make and model) and writes this to the UI*
> - at startup, it gets the serial number and writes this to the UI*
> - at startup, it calculates the maximum voltage and writes this to the UI*
> - at startup, it gets current voltage and writes this to the UI*
> - at startup, it gets the article number and writes this to the UI*
>
> \* these shall be visible in the UI until the UI closes.
>
> Enable the following functionality:
> - switch power output on and off
> - switch remote control on and off
>
> Enable UI for: get and set current voltage

Hardware: an Elektro-Automatik **PS 2000B** series bench power supply (this unit reports as
`PS 2084-03B`, 84 V / 3 A), connected over USB-serial (appears as `/dev/tty.usbmodemXXXXXXXX`
on macOS — a new physical unit gets a **different** port name).

## Repo layout

```
PS2000Test.sln
├── PS2000Test/                  Original exploratory console app — untouched, kept as a
│   └── Program.cs               working reference for the raw protocol bytes.
│
└── PS2000Test.Ui/                Blazor Server GUI (the actual deliverable)
    ├── Services/Ps2000Client.cs  Protocol layer: builds/parses telegrams, owns the serial port
    ├── Components/Pages/
    │   ├── Home.razor            The (only) page: UI state, polling loop, event handlers
    │   └── Home.razor.css        Dark instrument-panel theme (scoped CSS)
    ├── appsettings.json          Ps2000:PortName — MUST be updated per physical device, see below
    └── Program.cs                ASP.NET Core startup, registers Ps2000Client as a singleton
```

Two-layer split: `Home.razor` knows nothing about the wire protocol, only calls named methods
like `Device.SetVoltageAsync(15)`. `Ps2000Client` knows nothing about UI state.

## Protocol reference (PS2000B binary telegram protocol)

Confirmed against the official "PS 2000B Programming Guide" / "PS 2000B object list" (via
`github.com/ssproessig/Python-PS2000B`, a reference implementation that cites both documents
directly), and validated against the real device during this session.

- **Serial settings:** 115200 baud, **odd parity**, 8 data bits, 1 stop bit. (Getting parity
  wrong — it was `None` originally — was the root cause of corrupted/empty reads early on.)
- **Telegram shape:** `SD, DN, OBJ, [DATA...], checksumHigh, checksumLow`. Checksum = sum of
  every preceding byte, split into two bytes.
- **SD (start delimiter) byte:** bits 0-3 = length nibble, bit 4 = direction, bit 5 = cast
  type, bits 6-7 = transmission type (`01` = query, `11` = send/control).
  - Query: `SD = 0x70 | (expectedResponseLength - 1)`. The nibble is the length you *expect
    back*, not what you're sending (a query only ever sends DN+OBJ).
  - Send/control: `SD = 0xF0 | (payloadLength - 1)`, where payload is the parameter bytes
    after DN+OBJ.
- **Response layout:** `[SD, DN, OBJ_echo, DATA..., CSHi, CSLo]`. Data = everything between
  byte index 3 and the trailing 2 checksum bytes — computed from the actual response length,
  not assumed from the request (the device doesn't always honor the requested length exactly).

| Object | # | Notes |
|---|---|---|
| Device type | 0 | Identity string, e.g. `PS 2084-03B` |
| Serial number | 1 | Identity string |
| Nominal (max) voltage | 2 | 4-byte big-endian float |
| Article number | 6 | Identity string |
| Control (remote/output) | 54 (0x36) | 2 param bytes: `p1` = which switch, `p2` = value |
| Status / actual values | 71 (0x47) | Returns remote-active bit, output-active bit, live voltage/current words |
| Set voltage | 50 (0x32) | **Not independently confirmed** — ported from the original exploratory console app. The reference implementation never implemented "set" at all. Works in practice; if a future session wants to fully verify it, this is the one object number taken on faith rather than cross-checked. |

Control object (54) parameters:
- Remote control: `p1 = 0x10`, `p2 = 0x10` (on) / `0x00` (off)
- Power output: `p1 = 0x01`, `p2 = 0x01` (on) / `0x00` (off)

Identity strings: take the full DATA field, ASCII-decode, trim trailing `\0`/spaces.

Voltage: status query returns a 16-bit word; `percent = word / 256.0` (0-100 range);
`voltage = nominalVoltage * percent / 100`. Setting: `word = round(25600 * volts / nominal)`,
sent as big-endian hi/lo bytes (this is the inverse of the read formula: `25600 = 256 * 100`).

## `Ps2000Client` design notes

- **Singleton with a persistently-open port**, not opened/closed per telegram. Reopening 5-7
  times in a row (once per startup query) was slow and, under Blazor Server's double render (a
  static prerender immediately followed by the real interactive render, each running its own
  init), raced for the OS handle → `"Access to the port is denied"`.
- **`SemaphoreSlim(1,1)`** serializes all port access so concurrent calls queue instead of
  colliding — this is a single physical UART link, there is no parallelism to exploit.
- **Self-healing on fault**: if a write/read throws (flaky USB — this specific unit's
  connection drops intermittently), the cached `SerialPort` is disposed and nulled so the next
  call opens a fresh one, rather than repeatedly hitting a dead handle (`"port is closed"`
  forever otherwise).
- `GetData()` computes the data-field length from the *actual* response byte count, not the
  requested length — the device doesn't always honor what you asked for.

## UI behavior (`Home.razor`) — and why

- **Startup** reads device type, serial, article number, nominal voltage once (`InitializeAsync`,
  called from `OnInitializedAsync`); these stay in fields and are rendered unconditionally
  (never cleared) for the life of the page, satisfying the "visible until UI closes" requirement.
- **On connect, the app does NOT command the device into a known state.** It only reads
  (`RefreshStatusAsync`) and reflects whatever the device actually is — output may already be
  live (e.g. someone set a voltage on the physical front panel, possibly into a real load), and
  silently forcing it off just because the app was opened would be a worse surprise than
  leaving it alone. Remote control likewise starts however it actually is. *(This was
  deliberately reversed mid-project — an earlier version force-reset remote-on/output-off on
  every connect for a "known safe state"; the current, intended flow is: device connects →
  physical front-panel has control → user enables Power Output physically → user enables
  Remote Control from the UI, which hands control to the app and locks out the front panel.)*
- **Continuous live polling**: a background loop (`PollLoopAsync`, started after a successful
  connect, cancelled via `IDisposable` when the page closes) calls `RefreshStatusAsync` in a
  cycle of *(await the read) → 300ms delay → repeat*, not a fixed-interval `Timer`. This keeps
  the polling paced to what the single serial link can actually sustain rather than piling up
  overlapping calls if a read is slow. Gives roughly 2-3 updates/second. This drives the live
  "Output Voltage" hero number and the power/remote toggle states — so if someone changes
  something on the unit's own front panel, the UI catches up within one poll cycle.
- **The "Set Voltage" input field is intentionally decoupled from the live poll.** It is seeded
  from a `GET` (the device's actual current value, not reset to 0) at exactly one moment: when
  Remote Control transitions from off to on (`RefreshStatusAsync` detects this transition
  itself: `status.RemoteControlActive && !_remoteOn`). After that the field is never
  overwritten by the poll — only by the user typing or by `ApplyVoltageAsync` after a
  successful `Set`. Getting this wrong earlier (syncing the field on *every* poll tick) was a
  real bug: it looked like the input box was "changing on its own" right after the user typed
  into it and clicked Set, because the live-measured value (briefly different during the
  supply's own settling/ramp time) kept overwriting what they'd just typed.
- **Power Output and Remote Control toggles** are disabled in the UI while Remote Control is
  off (matches the device's own constraint — control commands over serial require remote mode).
- Errors from any command are shown inline (`_controlError` / `_voltageError`) rather than
  crashing the page; a failed *startup* connect shows a full "Could not reach the device" panel
  with a Retry button.

## Styling

Dark "instrument panel" theme — IBM Plex Sans/Mono, oklch-based dark palette, defined as scoped
CSS in `Home.razor.css`. `wwwroot/app.css` has a small global reset (`html, body { margin: 0;
background: <same dark color>; }`) — without it there was a white strip around the app because
the browser's default body margin left the page background showing outside the dashboard div.

## Known gaps / next things to check

1. **Set Voltage's OBJ/SD byte (50 / `0xF2`-derived) is not independently verified** against
   the official object list — see the protocol table above. Works empirically; worth
   confirming against the actual PDF object list if there's ever a discrepancy.
2. **The physical connection to this specific test unit is flaky** — it has dropped and
   re-enumerated under a different `/dev/tty.usbmodemXXXXXXXX` name multiple times during
   development. `appsettings.json` → `Ps2000:PortName` needs to be updated to match whatever
   the current device shows up as (`ls /dev/tty.usb*` on macOS when it's plugged in).
3. **Two Blazor-Server-specific gotchas already solved, worth remembering if new symptoms
   appear that look similar:** (a) the double prerender+interactive render both running
   `OnInitializedAsync` can look like duplicate/racy device commands — that's normal Blazor
   Server behavior, not a bug; (b) a browser tab whose SignalR circuit has dropped (e.g. after
   the dev server was restarted) will look completely unresponsive to clicks with no console
   error obviously visible — reload the page rather than assuming the button handler is broken.
4. **Polling interval (300ms)** is a single constant in `PollLoopAsync` — easy to tune if 2-3
   updates/sec isn't the right feel.
5. Running via Rider: make sure **`PS2000Test.Ui`** (not the old console project) is the
   selected startup project. If port 5041 is reported busy on start, check for a leftover
   `dotnet run` process from a previous session holding it.

## Requirements checklist

| Requirement | Status |
|---|---|
| Device type shown at startup, stays visible | ✅ |
| Serial number shown at startup, stays visible | ✅ |
| Max voltage calculated/shown at startup, stays visible | ✅ |
| Current voltage shown at startup, stays visible (and live) | ✅ |
| Article number shown at startup, stays visible | ✅ |
| Power output on/off | ✅ |
| Remote control on/off | ✅ |
| Get/set current voltage | ✅ |
