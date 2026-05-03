# amChipper v0.2.1.0-AMC20260503.3

This release repairs the advanced Settings surface by removing non-functional controls, adding a real sidebar chiptune library, and improving the advertised NSFE/NSF path.

## Included

- Added a real left-sidebar Library tab for configured chiptune folders.
- Added library actions for adding/removing folders, refreshing the sidebar, filtering files, and opening SID/NSF/NSFE/tracker/MIDI/FSC files directly from the sidebar.
- Wired the Settings chiptune-library folder list to the same add/remove/refresh commands.
- Added browse actions for the user-data folder and external tracker tool path.
- Made autosave create timestamped `.amchip` backup snapshots on the configured cadence and before export when that mode is selected.
- Wired restore-previous-state and silent-startup behavior to real launch behavior.
- Removed misleading Settings controls for theme-engine/system options that were only persisted but not applied.
- Replaced them with appearance controls that are already live in the app: theme, workspace density, toolbar sizing, button shine, and panel shadows.
- Added NSFE chunk normalization so straightforward `.nsfe` files are converted into NSF-compatible data for metadata, import, rendering, and live playback instead of falling back to digest audio.
- App build bumped to `v0.2.1.0-AMC20260503.3`.
- SID plugin assembly bumped to `v0.2.1.0`.
- NSF plugin assembly bumped to `v0.2.2.0`.

## Notes

SID/NSF emulation and chip-to-tracker reconstruction are still active development areas. This build does not claim full hardware parity, but it removes a false-positive `.nsfe` support path and makes the library workflow usable from the main window instead of burying folder paths in Settings.
