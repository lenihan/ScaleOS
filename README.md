# ScaleOS (sos)

> A PowerShell-powered terminal operating environment where graphical applications live in scrollback history.

ScaleOS reimagines the terminal as a complete operating environment. Applications render directly inside the terminal, support mouse input, graphics, and audio, and leave behind persistent artifacts in a scrollable timeline.

Instead of switching between windows, users navigate through history. Applications can be revisited, resumed, and interacted with long after they have been closed.

Core ideas:

- Terminal-first computing
- Graphical applications inside the terminal
- PowerShell as the application platform
- History as the desktop
- Persistent and resumable application state
- Cross-platform (Windows, Web, Raspberry Pi)

## Getting started (Windows)

Requires the .NET 10 SDK. Run it in Windows Terminal (it needs truecolor and mouse support).

```powershell
dotnet build
dotnet run --project src/Sos.Core
```

Try the proof of concept:

1. Type `notes`, add a few notes (type + Enter), then press **Esc** to close the app.
2. Scroll up (mouse wheel or PgUp). The closed app is still in the timeline.
3. Quit with **Ctrl+Q** (or `exit`), start ScaleOS again, and the history is still there.
4. Click **[ Resume ]** on the artifact (or double-click it, or run `Resume-App <id>`), and the notes come back.

Background apps: type `timer`, enter `0.5 tea` to start a 30-second countdown, then press **Ctrl+Z**. The timer
keeps running in the background (still visible, dimmed, in its timeline entry). When it ends, a notification
appears in the timeline; click it to bring the timer back at the bottom.

Graphics and sound: type `invaders`. Use **←/→** to move, **Space** to fire, and **P** to pause. **Ctrl+Z** pauses
the game and sends it to the background; close or quit mid-game and **[ Resume ]** puts you back where you were.
Set `SOS_MUTE=1` to turn sound off.

Other keys: **Tab** switches between the prompt and the live app, and **PgUp/PgDn** scroll.
Useful commands: `Get-Timeline`, `Get-SosApp`, `Start-SosApp <name>`.
Data lives in `%USERPROFILE%\.sos` (override with `--home <dir>`). The host log is in `logs\host.log`.

## Running on Linux (WSL)

Publish a self-contained build on Windows (the Linux side doesn't need .NET or PowerShell installed), then run it
from WSL:

```powershell
./scripts/publish.ps1 -Runtime linux-arm64    # ARM PC; use linux-x64 on Intel/AMD
wsl
```

```bash
cd /mnt/c/<path-to-repo>/out/linux-arm64
./sos
```

For sound in WSL, install a player: `sudo apt install pulseaudio-utils` (ScaleOS posts a timeline notification if
it can't find one). The same `linux-arm64` build is what a 64-bit Raspberry Pi runs.

Troubleshooting: set `SOS_TRACE=1` to log every message from the PowerShell host to `logs/core.log` in the
data folder (next to `logs/host.log`).

### Layout

| Path | What it is |
| --- | --- |
| `src/Sos.Core` | `sos.exe`: terminal, rendering, input, timeline, app lifecycle (no PowerShell, starts fast) |
| `src/Sos.Host` | `sos-host.exe`: hosts PowerShell; one runspace per app, plus the shell |
| `src/Sos.Protocol` | JSON-lines messages and the widget tree shared by both |
| `src/Sos.Sdk` | PowerShell module: widget builders (`New-Panel`, `New-List`, `New-Canvas`, …), `Play-Sound`, and shell commands |
| `apps/` | Apps, one folder per app: `NotesPS`, `TimerPS`, `InvadersPS` |
| `tests/` | Unit tests, plus end-to-end tests that run the real core and PowerShell host |

Writing an app: see the contract at the top of [Sos.Sdk.psm1](src/Sos.Sdk/Sos.Sdk.psm1) and the
[NotesPS](apps/NotesPS/NotesPS.psm1) (simplest), [TimerPS](apps/TimerPS/TimerPS.psm1) (background work) and
[InvadersPS](apps/InvadersPS/InvadersPS.psm1) (graphics, animation, sound) examples.
