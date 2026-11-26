# TempBar

A small Windows tray app that shows CPU and GPU temperatures right on the taskbar, next to the notification area:

```
CPU 54°   GPU 61°
```

- Text follows the taskbar's light/dark theme. Values at or above 85 °C turn red.
- Hides itself during fullscreen games and presentations, and when the taskbar auto-hides.
- Drag the readout left or right along the taskbar. Right-click it (or the tray icon) for options.
- If you turn off "Show on taskbar", the tray icon shows the CPU temperature instead.

## Resource use

- Reads only the CPU and the main GPU (NVIDIA, then AMD, then Intel). Motherboard, disks, RAM and network are never opened.
- Sensors are polled on one low-priority background thread, every 2 s by default (1/2/5/10 s in the menu).
- The overlay only repaints when a value, the theme, or the DPI changes.
- Workstation GC with concurrent GC turned off, and invariant globalization.

## Requirements

- Windows 10 or 11, x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for the small build. The self-contained build does not need it.
- **PawnIO driver for CPU temperature.** LibreHardwareMonitor needs it for ring-0 sensor access. GPU temperature works without it.

  ```powershell
  winget install namazso.PawnIO
  ```

- Administrator rights. The exe asks for elevation (UAC) when it starts.

## Build

On Windows with the .NET 8 SDK:

```powershell
.\build.ps1                 # dist\TempBar.exe, ~5 MB, needs the .NET 8 Desktop Runtime
.\build.ps1 -SelfContained  # dist\TempBar.exe, ~68 MB, no prerequisites
```

From Linux or macOS (cross-compiles the Windows exe):

```bash
./build.sh
./build.sh --self-contained
```

During development: `dotnet run --project src/TempBar` from an elevated terminal.

## Releases

`.github/workflows/release.yml` builds both exes on every push and pull request; they show up as a downloadable artifact on the run. Pushing a `v*` tag also creates a GitHub release with both exes attached:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## Menu

| Item | What it does |
| --- | --- |
| Show on taskbar | Turns the taskbar readout on or off |
| Fahrenheit | Switches between °C and °F |
| Update every | Polling interval |
| Reset position | Puts the readout back next to the notification area |
| Start with Windows | Adds a Task Scheduler logon task with highest privileges, so there's no UAC prompt at boot |

Settings are saved to `%AppData%\TempBar\settings.json`.

## Notes

- Only the taskbar on the main monitor is used.
- On Windows 11 the readout sits about 260 px from the right edge by default. Drag it if it covers the tray icons.
- Sensor access comes from [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0).
