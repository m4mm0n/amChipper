# amChipper

amChipper is a Windows/.NET 10 WPF chiptune DAW and tracker project by Geir Gustavsen / ZeroLinez Softworx. The goal is a modern tracker workflow with MilkyTracker-style pattern editing, FL Studio-inspired arrangement/piano-roll controls, chip format import/export, live mixer/analyzer views, localization, and a native compressed amChipper module format.

## Current focus

- Tracker-style editing for patterns, channels, instruments, effects, piano-roll views, and playlist arrangement.
- Native project format (`.amc`) for compressed amChipper modules.
- Playback/import paths for tracker and chip formats, including XM/MOD/S3M/IT through libopenmpt and internal SID/NSF/amChipper player libraries.
- Export paths for native modules, XM conversion, WAV/MP3 rendering, MIDI, and FL Studio score exchange where the format permits it.
- WPF desktop UI with themes, localization, settings persistence, splash/loading flow, help/about screens, and release packaging.

Some emulator/conversion paths are still under active development. Exact tracker round-tripping is strongest for module formats that expose editable pattern data directly; SID/NSF conversion is necessarily reconstructed from chip playback/state analysis.

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK.
- Visual Studio 2026 or a recent Visual Studio build that supports .NET 10 WPF projects.
- Optional: libopenmpt native runtime for tracker playback where the project expects it.

## Build

```powershell
dotnet restore .\amChipper.sln
dotnet build .\amChipper.sln -c Release -p:Platform=x64
dotnet test .\amChipper.sln -c Release -p:Platform=x64 --no-build
```

To publish a local release folder:

```powershell
dotnet publish .\src\amChipper.App\amChipper.App.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o .\Ready2Release
```

The app intentionally moves dependency DLLs into `libs/` during build/publish and normalizes the runtime dependency map for that layout.

## Repository layout

- `src/amChipper.App` - WPF application and UI.
- `src/amChipper.Core` - tracker/project model, format metadata, import/export logic.
- `src/amChipper.Audio` - audio runtime integration.
- `src/amChipper.AmcPlayer` - native amChipper module playback support.
- `src/amChipper.SidPlayer` - internal SID support.
- `src/amChipper.NsfPlayer` - internal NSF support.
- `src/amChipper.LanguageTool` - translation editor tooling.
- `src/QuickLog` - Git submodule for the QuickLog logging library used by the app.
- `tests/amChipper.Core.Tests` - core regression tests.
- `docs/` - GitHub/wiki-ready documentation.

## Third-party music corpora

Large SID/NSF/tracker corpora used for local analysis are deliberately ignored by Git. They are useful for testing, but they cannot automatically be licensed as GPLv3 source unless their own rights permit it. Keep those files locally and point amChipper at them from settings or import/open workflows.

## License and commercial rights

The public source code in this repository is licensed under GPL-3.0-only. See `LICENSE`.

GPLv3 protects the open-source version by requiring redistributed modified versions to keep the same source-code freedoms. It does not prohibit charging money for copies or commercial use of the GPL version. For future proprietary/commercial releases by Geir Gustavsen / ZeroLinez Softworx, this repository uses a dual-licensing-friendly contribution model: contributors must agree to the contributor terms in `CONTRIBUTOR_TERMS.md`, which grant the project owner rights to relicense contributed changes separately.

This is not legal advice. Before accepting outside contributions or shipping a commercial edition, have the contributor/relicensing text reviewed properly.
