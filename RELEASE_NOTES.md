# amChipper v0.1.0.0-AMC20260502.10

This release focuses on stopping NSF playback from freezing the WPF app and keeping the release package current.

## Included

- NSF song playback now uses the imported trace sequencer path instead of forcing a synchronous rendered WAV cache.
- Opening NSF files no longer starts a background preview render automatically.
- NSF trace import has tighter wall-clock budgets and exits after repeated play-call timeouts.
- NSF WAV/MP3 conversion uses the trace sequencer path by default to avoid renderer lockups.
- App build bumped to `v0.1.0.0-AMC20260502.10`.
- SID and NSF plugin assemblies bumped to `v0.1.7.0`.
- Ready2Release has been rebuilt, language packs regenerated, and the published executable smoke-tested.

## Notes

SID/NSF emulation and chip-to-tracker reconstruction are still active development areas. This build prioritizes keeping the DAW responsive for problematic NSF drivers while preserving editable trace playback and live meters.
