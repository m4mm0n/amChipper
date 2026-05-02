# amChipper Wiki

amChipper is a modern chiptune DAW/tracker for Windows built on .NET 10 and WPF.

## Quick links

- [Building](Building.md)
- [Architecture](Architecture.md)
- [Format Support](Formats.md)
- [Release Process](Release.md)
- [Localization](Localization.md)
- [Commercial Licensing](Commercial-Licensing.md)
- [Samples](samples.md)

## Product shape

The application combines tracker editing with modern DAW surfaces:

- Project Hub for project metadata, workflow status, and quick actions.
- Playlist for song arrangement.
- Channel Rack for instruments/channels.
- Automation Rack for live/editable automation lanes.
- Mixer and Analyzer for channel state and visual monitoring.
- Piano Roll for modern note editing.
- Tracker Editor for raw row/effect editing.
- Settings, localization, about/help, changelog, and log viewing.

SID/NSF support is internal and under active development. Direct editable patterns for those formats are reconstructed where possible because those chip formats do not naturally store tracker rows like XM/MOD/S3M/IT.

## What ships in the release zip

The downloadable package is the `Ready2Release` folder:

- `amChipper.exe` and the app runtime files at the root.
- `libs/` for dependency DLLs loaded by the custom runtime resolver.
- `lang/` for editable language packs.
- `tools/` for the language tooling.
- `Examples/` for bundled example material that is safe to redistribute.
- `CHANGELOG.md` and `USER_GUIDE.txt` for offline help inside the app.
