# PS2000
IKT300 (Software architecture) assignments. Connecting to a power supply remotely and controlling it, focusing on building good maintainable architecture.

## Projects

| Project | Role |
|---|---|
| `PS2000Test.PowerSupply` | `IPowerSupply` + implementations (real PS2000, dummy, MQTT) behind `PowerSupplyFactory` |
| `PS2000Test.Ui` | Blazor dashboard. Only talks to `IPowerSupply`, never to a concrete device |
| `PS2000Test.PsuAgent` | Console app that owns the PSU and mirrors it to/from MQTT |
| `PS2000Test` | Original exploratory console app, kept as a raw protocol reference |

## Running and testing

Needs the .NET 10 SDK. The UI listens on `http://localhost:2000` (see `launchSettings.json`).

### A. UI with the dummy (no hardware, no broker)
```bash
dotnet run --project PS2000Test.Ui
```
Open the UI and pick **Dummy** on the start page. Switch remote control on, set a voltage, switch
output on - the dummy only follows the set point while output is enabled.

### B. UI with the real PSU (USB)
Same command. The start page scans the serial ports and lists any PS2000 it finds; pick it.

### C. UI and PSU on different machines (MQTT)
**1. Broker (once, on the machine that hosts it).** Mosquitto only listens on localhost by default.
With Homebrew there is no `mosquitto.conf` until you create it
(`/opt/homebrew/etc/mosquitto/mosquitto.conf`, containing the two lines below), then
`brew services restart mosquitto`:
```
listener 1883
allow_anonymous true
```
Find the broker machine's IP with `ipconfig getifaddr en0`.

**2. PSU agent (on the machine the PSU is attached to):**
```bash
dotnet run --project PS2000Test.PsuAgent -- <broker-host> --dummy
dotnet run --project PS2000Test.PsuAgent -- <broker-host> <serial-port>
```
Use `localhost` as broker host when the agent runs on the broker machine.

**3. UI (on any machine that can reach the broker).** Run the UI as in A, then use the **MQTT**
box on the start page: broker host = the broker's IP, PSU id = the PSU's serial number
(`DUMMY0000` for the dummy).

### D. Checking the MQTT side without the UI
Connect [MQTT Explorer](https://mqtt-explorer.com/) to `mqtt://<broker-host>:1883` and look under
`/PSU/PSU2000/<psuId>/`:

| Topic | Retained | Direction | Content |
|---|---|---|---|
| `info` | yes | agent → UI | JSON: device type, serial, article number, nominal voltage |
| `status` | yes | agent → UI | JSON: remote, output, voltage percent (~every 300 ms) |
| `online` | yes | agent → UI | `1` while the agent runs, `0` after it stops or dies (last will) |
| `cmd/voltage` | no | UI → agent | volts, e.g. `30` |
| `cmd/output` | no | UI → agent | `1` / `0` |
| `cmd/remote` | no | UI → agent | `1` / `0` |

To test by hand, publish to a `cmd/...` topic **with retain unchecked** and watch `status` change.
Commands are deliberately not retained: a retained command would be replayed when the agent
restarts and could switch the output on or change the voltage unasked.

Something worth trying: stop the agent with Ctrl+C and watch `online` go to `0`.

### Known limitation
If the agent dies while the dashboard is open, the UI keeps showing the last values instead of
"Disconnected" - the poll loop in `Home.razor` swallows read errors.
