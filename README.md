# Syntra

Colour, vibrance, resolution, monitor and crosshair tools for Windows 10 / 11 — in one `.exe`.

## ⬇ Download

**[Download Syntra.exe](https://github.com/xomaim/Syntra/releases/latest/download/Syntra.exe)**

That's the only file. No installer, no Python, no .NET to install. Double-click it.
Windows SmartScreen may warn because the file isn't code-signed: click **More info → Run anyway**.

## What it does

| Page | What you get |
|---|---|
| **Display** | Saturation 0–300 %, vibrance (uses NVIDIA's own Digital Vibrance when present), brightness, contrast, gamma, temperature, highlights, shadows, blacks, whites, presets, save your own looks |
| **Resolution** | All modes your display supports, refresh rates, stretched 4:3 presets, custom mode. Every change reverts after 15 s unless you click Keep |
| **Monitor** | Your monitor's real brightness/contrast over DDC/CI, low-blue-light |
| **Crosshair** | Click-through overlay crosshair: cross / T / dot / circle, size, gap, thickness, colour, outline |
| **Profiles** | A look per game — applied while that game is in front, put back when you leave it |
| **Settings** | Launch with Windows, Ctrl+Alt+V on/off, "put my screen back", repair, SHA-256 of the running copy |

## What Syntra never does

* It never reads a game's memory.
* It never changes a game's files.
* It never puts code into a game, or into any other program.
* It installs no driver.
* It installs and runs as your own Windows user. It needs no admin rights. One repair button can restart it as admin. Windows asks you first.

### How that's possible

Syntra only uses ordinary Windows features that change **what the monitor shows**:

* **Colour matrix** — `MagSetFullscreenColorEffect` (Windows' own compositor, same as the accessibility colour filters).
* **Gamma ramp** — `SetDeviceGammaRamp`.
* **NVIDIA vibrance** — the `nvapi64.dll` that already ships with your NVIDIA driver (same setting as NVIDIA Control Panel).
* **Resolution** — `ChangeDisplaySettingsEx`, dynamic only.
* **Monitor** — DDC/CI via `dxva2.dll`, like pressing the monitor's buttons.
* **Profiles** — looks at the *name* of the program in front (like Task Manager). Never opens it.
* **Crosshair** — a separate transparent, click-through window.

Settings live in `%AppData%\Syntra\settings.json`. Closing the window keeps Syntra in the tray; **Exit** from the tray puts your screen back exactly as it was.

## Building it yourself

Every push to `main` builds `Syntra.exe` on GitHub Actions and puts it on the [latest release](https://github.com/xomaim/Syntra/releases/latest).
Locally: `dotnet publish src/Syntra.csproj -c Release -o out` (needs the .NET 8 SDK, Windows).
