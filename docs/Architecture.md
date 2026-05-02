# Architecture

## Projects

- `amChipper.App` - WPF shell, windows, views, view models, theme/localization/settings UI.
- `amChipper.Core` - song/project model, tracker rows, patterns, format catalog, serializers, import/export helpers.
- `amChipper.Audio` - playback abstractions and runtime glue.
- `amChipper.AmcPlayer` - native `.amc` playback support.
- `amChipper.SidPlayer` - internal SID parser/player/emulation support.
- `amChipper.NsfPlayer` - internal NSF parser/player/emulation support.
- `amChipper.Console` - headless diagnostics/test harness.
- `amChipper.LanguageTool` - translation authoring helper.
- `QuickLog` - bundled logging infrastructure.

## Design boundaries

Tracker modules such as XM/MOD/S3M/IT carry editable pattern structure. Chip formats such as SID/NSF mostly carry code/register playback behavior, so the app treats their tracker views as reconstructed analysis rather than perfect original tracker source.

The `.amc` format is the project-owned path for richer editable data, compact storage, and future amChipper-specific instrument/effect metadata.
