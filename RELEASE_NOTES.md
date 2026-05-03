# amChipper v0.2.0.0-AMC20260503.2

This release hardens the v0.2 NSF live-playback path and cleans up the Settings surface so it stays focused on chiptune work instead of irrelevant VST-style defaults.

## Included

- NSF live stream rendering now runs on a background producer with a ring buffer.
- The audio callback now drains buffered samples only, which keeps playback/UI responsive even when a difficult NSF driver stalls briefly.
- NSF transport position and live visualizers continue to follow consumed audio frames.
- Removed irrelevant VST/VST3 default search folders from Settings.
- Replaced the folder defaults with chiptune/module library locations: Music/Chiptunes, Documents/amChipper, Examples, NSF, and SID.
- Existing saved settings are migrated away from the old VST/VST3 defaults when loaded.
- Renamed the affected settings to chiptune library / external tracker-tool wording.
- App build bumped to `v0.2.0.0-AMC20260503.2`.
- NSF plugin assembly bumped to `v0.2.1.0`.
- Ready2Release has been rebuilt and the published executable smoke-tested.

## Notes

SID/NSF emulation and chip-to-tracker reconstruction are still active development areas. This build targets the remaining NSF hang reports by isolating NSF emulation from the realtime audio callback; audio quality and exact hardware parity remain ongoing work.
