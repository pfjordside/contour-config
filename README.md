# Contour Config

Windows tool for the **Contour RollerMouse Red Plus Wireless** that speeds up cursor drift at the left and right ends of the roller bar. It avoids Contour's Immediate setting, which makes the cursor jump when an end is touched by accident.

## How it works

Contour's drift runs in the mouse firmware. The official tool can only choose Off, Slow, Fast or Immediate. No finer speed parameter is known, so this app does not write to the device.

The mouse reports roller-end contact on its consumer-control HID collection (Bluetooth `VID 0B33`, `PID 1006`, collection `COL03`). In input report `0x03`, byte 7 is a bitfield:

| Bit    | Meaning                                    |
|--------|--------------------------------------------|
| `0x08` | Right end held (confirmed from a capture)  |
| `0x04` | Left end held (likely; verify with Learn)  |
| `0x01` | Unknown short pulses, ignored              |

While an end marker is held longer than the activation delay, the app injects extra horizontal movement with `SendInput` at the configured speed. Contour's own drift continues underneath. When the marker clears, the extra movement stops at once.

## Run

Requires Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet run --project .\src\ContourConfig.App
```

No administrator access, driver installation, device writes or network access is needed. The app must stay running for boosting to work. Minimizing is fine; closing stops it.

## First use

1. In Contour's tool, set cursor drift to **Slow or Fast**, not Immediate. Markers may not be sent when drift is Off.
2. Start the app. **Device** should show *Connected*. If the mouse was asleep, move it and click **Reconnect**.
3. Hold each roller end. The monitor should show `[LEFT]` or `[RIGHT]`. If not, click **Learn left end** or **Learn right end** and hold that end for 4 seconds.
4. Set the extra speed for each side (0–8000 pixels/second) and the activation delay (0–2000 ms, default 300 ms). A longer delay ignores brief accidental touches.
5. Tick **Enable boost** and test. Click **Save settings** to keep the speeds and marker mapping.

**Emergency stop: Ctrl+Alt+F10**, the **Stop boost** button, or close the window. Boost always starts off. Power or session changes and disconnects stop it. A continuous hold stops boosting after 15 seconds as a safety limit.

## Limitations

- Only tested over Bluetooth. The USB receiver probably uses a different PID or path.
- The meaning of `0x04` (left) is not yet confirmed from a capture. Use the monitor and the Learn buttons.
- Elevated windows and secure desktops may reject injected movement.
- Windows pointer speed and acceleration also affect injected movement.

## Settings

Stored at `%LOCALAPPDATA%\ContourConfig\settings.json` (version 2: options and marker masks). Version 1 files from the old calibration prototype are migrated: their speeds and delay are kept and the old calibration data is ignored. Invalid settings show an error and are never silently applied.

## Development

```powershell
dotnet build .\ContourConfig.slnx
dotnet test .\tests\ContourConfig.Core.Tests.Unit
```

`src\ContourConfig.Core` holds the marker decoding (`EndMarkerMap`), time-based boosting (`EndDriftBooster`), marker learning and settings. `src\ContourConfig.App` holds the WPF UI, HID reader (`RollerMouseHid`) and `SendInput` integration. There are no third-party runtime packages. Tests use xUnit.

Do not send speculative HID feature reports to the device: their meaning and side effects are unverified.
