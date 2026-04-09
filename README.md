# BatteryMeter

Lightweight system tray app that shows real-time battery charge/discharge wattage on Windows.

Built to diagnose USB-C PD negotiation issues — specifically intermittent charge/discharge cycling when using non-OEM chargers on Surface devices.

## Screenshot

Tray icon shows wattage as a number: **green** = charging, **orange** = discharging, **silver** = idle.

Hover tooltip shows:
```
⚡ +5.6W │ 11.2V │ AC
2,610 mWh (6%)
▁▂▃▄▅▆▇█████████
```

## Features

- Real-time wattage in the system tray icon (color-coded)
- 500ms polling via WMI `BatteryStatus`
- Tooltip with charge/discharge rate, voltage, AC status, capacity, and sparkline history
- Right-click menu: Refresh, Copy stats, Start with Windows, Exit
- Sparkline chart of recent battery capacity (~5 min history)
- Single-file publish (~150KB exe)

## Requirements

- Windows 10/11
- .NET 10 runtime

## Build & Run

```powershell
.\run.ps1
```

Or manually:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
.\bin\Release\net10.0-windows\win-x64\publish\BatteryMeter.exe
```

## Data Sources

| Source | Data |
|--------|------|
| WMI `BatteryStatus` | ChargeRate, DischargeRate, Voltage, Charging, PowerOnline, RemainingCapacity |
| WMI `BatteryFullChargedCapacity` | Full capacity (for % calculation) |
| `PerformanceCounter` Power Meter | Total system power draw (mW) |

## Project Structure

```
BatteryMeter.csproj    .NET 10 WinForms project
Program.cs             Entry point
BatteryTrayApp.cs      NotifyIcon lifecycle, tooltip, context menu
BatteryPoller.cs       WMI polling on a timer
TrayIconRenderer.cs    Renders wattage number onto tray icon
BatteryStatus.cs       BatteryReading record
TaskbarPanel.cs        Taskbar-embedded text panel (available, not active)
```
