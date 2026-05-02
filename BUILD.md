# amChipper — Build & Setup Guide

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| Windows | 10/11 (WPF requires Windows) |
| Visual Studio / Rider | Optional — `dotnet` CLI is sufficient |

## Clone & Build

```bash
cd amChipper
dotnet restore
dotnet build
```

## Run

```bash
dotnet run --project src/amChipper.App
```

Or open `amChipper.sln` in Visual Studio 2022+ and press **F5**.

---

## Logging (QuickLog)

amChipper uses **QuickLog** (`QuickLog` v1.0.2 on NuGet — `github.com/m4mm0n/QuickLog`).

Logs are written to:
```
%LOCALAPPDATA%\amChipper\Logs\amChipper.log
```

All log calls go through `AppLogger` (static façade in `src/amChipper.App/Services/AppLogger.cs`).
If you need to adjust the QuickLog API mapping (method names differ from assumptions),
edit only the two `// ── QUICKLOG …` regions inside that one file.

```csharp
// Usage anywhere in the App or Audio projects:
AppLogger.Info("Song loaded.");
AppLogger.Warning("No samples found in instrument.");
AppLogger.Error(ex, "Failed to render buffer");

// Audio/Core classes receive IAppLogger via constructor injection:
var engine = new AudioEngine(AppLogger.Instance);
```

---

## libopenmpt (module file playback)

**libopenmpt.dll is downloaded automatically on first run.**

On the very first launch amChipper checks whether `libopenmpt.dll` is present
next to the executable. If it is missing you will be offered a one-click
download (~3 MB) from lib.openmpt.org. A progress window tracks the download,
extracts the DLL from the release zip, and places it in the application folder.

If you prefer to install it manually:

1. Download the Windows binary from <https://lib.openmpt.org/libopenmpt/>
2. Copy `libopenmpt.dll` (x64) next to `amChipper.exe`, or into:
   ```
   src/amChipper.App/lib/libopenmpt.dll
   ```
   The `.csproj` copies it to the output directory automatically.

Without `libopenmpt.dll` the tracker still works for **creating** native songs —
the internal sequencer handles built-in chiptune synth instruments and imported
WAV samples without it.

---

## Project Layout

```
amChipper/
├── amChipper.sln
└── src/
    ├── amChipper.Core/          # Domain models, interfaces — no UI, no audio
    │   ├── Models/              # Song, Pattern, Track, Note, Instrument, Sample, Enums
    │   └── Interfaces/          # IAudioEngine, IModulePlayer
    ├── amChipper.Audio/         # Audio engine
    │   ├── Interop/LibOpenMpt.cs   # P/Invoke wrapper for libopenmpt.dll
    │   └── Engine/
    │       ├── AudioEngine.cs      # NAudio WaveOutEvent driver
    │       ├── ModulePlayer.cs     # libopenmpt module file player
    │       └── InternalSequencer.cs# Native sample-based sequencer
    └── amChipper.App/           # WPF application
        ├── Theme/DarkTheme.xaml    # Dark colour theme (Resource Dictionary)
        ├── Commands/RelayCommand.cs
        ├── ViewModels/             # MVVM view-models (no framework dependency)
        └── Controls/
            ├── Transport/          # Play/Stop/Pause/BPM bar
            ├── InstrumentBrowser/  # Left-panel instrument & sample list
            ├── PianoRoll/          # FL Studio-style piano roll editor
            │   ├── NoteGridCanvas.cs   # Custom rendered note grid
            │   ├── PianoKeysCanvas.cs  # 128-key piano sidebar
            │   └── VelocityCanvas.cs   # Velocity editor strip
            ├── SongEditor/         # FL Studio playlist / track board
            │   ├── TimelineCanvas.cs   # Pattern block timeline
            │   └── TrackHeaderPanel.cs # Track mute/solo headers
            └── PatternEditor/      # MilkyTracker-style tracker grid
                └── PatternGridCanvas.cs# Custom rendered tracker grid
```

## Keyboard Shortcuts (Pattern Editor)

| Key | Action |
|-----|--------|
| `Z`–`M` (bottom row) | Enter notes C4–B4 |
| `Q`–`U` (top row) | Enter notes C5–B5 |
| `[` / `]` | Decrease / increase base octave |
| Arrow keys | Navigate rows / channels |
| `Delete` | Clear current cell |

## Current Native Project Support

amChipper saves and opens native project files as:

```
*.amchip
```

This stores the editable song model, patterns, playlist blocks, instruments,
sample metadata, and built-in synth settings. IT/XM export is still separate
future work.

## Next Steps / Roadmap

- [ ] Undo/Redo stack (Command pattern over Song mutations)
- [ ] Sample waveform display in instrument browser
- [ ] Live preview: click piano key → play note through current instrument
- [ ] MIDI input support (NAudio MIDI device enumeration)
- [ ] Envelope editor UI for instrument volume/panning curves
- [ ] Song export to WAV via offline render
- [ ] IT/XM file export from Song model
