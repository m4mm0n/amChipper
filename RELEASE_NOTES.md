# amChipper v0.2.0.0-AMC20260503.1

This release starts the v0.2 line. It replaces the rough NSF song-playback fallback with bounded live NSF streaming and expands Settings into a broader DAW-style configuration surface.

## Included

- Added a live chip stream player in the audio engine for NSF source playback.
- NSF song playback now streams from the NSF driver path instead of the imported tracker trace when the file is clean and playback scope is Song.
- NSF pattern and piano-roll playback still use the editable trace path, so visible tracker/piano-roll material remains usable.
- The chip stream path is bounded per audio chunk and defers expensive NSF play calls after repeated timeouts instead of letting one driver monopolize playback.
- `nsf-batch` and `chip-batch` now validate NSF files through the same streaming path used by app playback.
- Added a regression test proving NSF stream chunks render audibly within a bounded time budget.
- Added advanced Settings sections for MIDI input/output, MIDI sync, audio mixer behavior, undo/history, startup behavior, file backups, browser folders, external tools, theme/scaling/animation controls, and project defaults.
- Persisted the new advanced settings through the existing configuration save/load/import/export flow.
- App build bumped to `v0.2.0.0-AMC20260503.1`.
- SID and NSF plugin assemblies bumped to `v0.2.0.0`.
- Ready2Release has been rebuilt, language packs regenerated, and the published executable smoke-tested.

## Notes

SID/NSF emulation and chip-to-tracker reconstruction are still active development areas. This build specifically targets the remaining NSF UI-hang and bad-song-playback path by using live bounded streaming for full-song playback and keeping the trace sequencer as the editable fallback.
