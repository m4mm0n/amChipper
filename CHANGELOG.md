# amChipper Changelog

This changelog covers the implementation work performed from 30.04.2026 through 03.05.2026. It is written as a user-facing development log for the current prototype rather than as a git-derived release log.

## v0.2.3.0-AMC20260503.6 - 03.05.2026

### Full SID Corpus Hardening

- Scanned `C:\Users\admin\OneDrive\Dokumenter\My FTPRush Downloads\_EVERYTHING` and found 118,092 SID-like files by extension.
- Added a dedicated `sid-batch` diagnostic so large SID libraries can be rendered without paying tracker-trace import cost for every file.
- Added `--sample-rate` and `--skip` batch options to make full-corpus validation deterministic and chunkable.
- Fixed real SID driver failures caused by unsupported unofficial 6510 `$4B`/ALR and `$AB`/LAX-immediate opcodes.
- Hardened SID filter, DC-block, smoothing, and soft-clip paths against non-finite audio state.
- Tightened pathological SID play-call throttling so repeated bad frames are deferred instead of burning the render loop.
- Re-ran the corrected post-fix SID corpus scan until stopped by request: 54,000 audible, 0 silent, 0 failed, 0 NaN outputs.
- The remaining SID range was not claimed as fully validated because continuing the full scan would have taken too long.
- Bumped the app informational version to `v0.2.3.0-AMC20260503.6`.
- Bumped `amChipper.SidPlayer.dll` and the SID plugin facade to `v0.2.3.0`.

## v0.2.2.0-AMC20260503.5 - 03.05.2026

### Full NSF Corpus Hardening

- Scanned `C:\Users\admin\OneDrive\Dokumenter\My FTPRush Downloads\_EVERYTHING` and found 4,682 `.nsf`/`.nsfe` files.
- Ran the existing live NSF batch path across the full corpus before the fix: 4,680 audible, 0 silent, 2 failed.
- Identified both failures as bank-switched NSF files with `Load=$0000` and valid high `Init`/`Play` routines.
- Changed the NSF program loader to allow `Load=$0000` only when an initial bank table is present, preserving rejection for invalid non-banked zero-load files.
- Added regression coverage for banked zero-load NSF streaming playback.
- Re-ran the full corpus after the fix: 4,682 audible, 0 silent, 0 failed.
- Bumped the app informational version to `v0.2.2.0-AMC20260503.5`.
- Bumped `amChipper.SidPlayer.dll` to `v0.2.2.0` and `amChipper.NsfPlayer.dll` to `v0.2.3.0`.

## v0.2.1.0-AMC20260503.4 - 03.05.2026

### Settings Repair, Sidebar Library, and NSFE Normalization

- Added a real left-sidebar Library tab backed by the configured chiptune folders.
- Added sidebar actions to add folders, remove folders, refresh the scan, filter the library, and open supported files directly from the library list.
- Wired the Settings chiptune-library folder list to the same add/remove/refresh commands so the setting now controls a visible workflow.
- Added browse buttons for the user-data folder and external tracker-tool path.
- Made autosave create timestamped `.amchip` backup snapshots using the selected backup location, including the "Before every export" mode.
- Wired "Restore previous state after launch" and "Silent startup" into launch behavior.
- Removed misleading Settings controls for GUI scaling, pop-up scaling, color maps, animations, transparent windows, high-visibility editor lines, small scrollbars, undo knob tweaks, recent-change sorting, and high-performance power behavior because those controls were only persisted and not meaningfully applied.
- Replaced the removed theme-engine section with live appearance controls for theme, workspace density, toolbar size, button shine, and panel shadows.
- Added NSFE chunk normalization so straightforward `.nsfe` INFO/DATA/BANK chunks are converted into NSF-compatible bytes for metadata, import, rendering, and live playback.
- Added regression coverage for NSFE import/render conversion.
- Bumped the app informational version to `v0.2.1.0-AMC20260503.4`.
- Bumped `amChipper.SidPlayer.dll` to `v0.2.1.0` and `amChipper.NsfPlayer.dll` to `v0.2.2.0`.
- Updated the GitHub Release workflow so the uploaded zip includes language packs, release documents, and libopenmpt native runtime DLLs.

## v0.2.0.0-AMC20260503.2 - 03.05.2026

### NSF Buffered Playback and Chiptune-Focused Settings

- Moved live chip-stream rendering off the realtime NAudio callback into a small background producer/ring-buffer path.
- Changed NSF song playback failure behavior so an expensive NSF driver can cause a short stream underflow instead of locking the app or audio callback while the 6502 renderer catches up.
- Kept playback position and visualizer timing advancing from consumed audio frames so NSF playback still drives transport, meters, and playlist/analyzer visuals.
- Removed the irrelevant VST/VST3 default search paths from Settings.
- Replaced the old browser-folder defaults with chiptune/module-focused locations for Music/Chiptunes, Documents/amChipper, Examples, NSF, and SID.
- Added a settings migration filter that strips the old VST/VST3 paths from existing saved configurations instead of preserving the bad defaults forever.
- Renamed the affected Settings labels from generic external/plugin wording to chiptune library and external tracker-tool wording.
- Bumped the app informational version to `v0.2.0.0-AMC20260503.2`.
- Bumped `amChipper.NsfPlayer.dll` to `v0.2.1.0`.
- Rebuilt and smoke-tested the Release publish layout before publishing the GitHub release asset.

## v0.2.0.0-AMC20260503.1 - 03.05.2026

### Live NSF Streaming, Advanced DAW Settings, and 0.2 Release

- Added a bounded live chip-stream renderer for NSF sources so full-song NSF playback no longer has to pre-render a WAV cache or fall back to the rough editable tracker trace.
- Added `ChipStreamPlayer` to the audio engine and routed the WPF wave provider through it before module/audio-file/sequencer playback when a live chip stream is active.
- Changed clean NSF Song-scope playback to load the original NSF bytes and stream them directly from the internal NSF driver path.
- Kept NSF pattern and piano-roll playback on the editable trace sequencer path so tracker-visible pattern material can still be inspected and played in isolation.
- Added per-buffer NSF render state so streaming playback keeps play-call timing across audio callbacks instead of restarting timing at every buffer.
- Tightened repeated NSF play-call timeout handling in the stream path by deferring expensive play calls after consecutive timeouts rather than blocking the audio/UI path.
- Updated `nsf-batch` and `chip-batch` diagnostics so NSF validation uses the same streaming path as app playback.
- Added `NsfStreamingRendererProducesBoundedAudibleChunks` to guard against stream chunks going silent or taking too long to render.
- Validated the streaming path against 250 local NSF files for 2 seconds each; all sampled files produced audible output and no failures.
- Expanded the Settings rack into a broader FL Studio-style control console with MIDI input/output, sync, audio mixer behavior, undo/history, startup, autosave, backup, external-tool, scaling, animation, color-map, project-template, and project startup controls.
- Added persistence for the new advanced DAW settings in `settings.json`, including browser search folders, MIDI sync settings, audio priority/resampling preferences, theme-engine preferences, and project defaults.
- Kept the existing instrument lab controls intact while making the main Settings tab catch up to the more advanced instrument/envelope/filter/LFO/polyphony surface already present in the side rack.
- Bumped the app informational version to `v0.2.0.0-AMC20260503.1`.
- Bumped `amChipper.SidPlayer.dll` and `amChipper.NsfPlayer.dll` to `v0.2.0.0`.
- Rebuilt `Ready2Release`, regenerated language packs, smoke-tested the published app executable, pushed the source update, and published the matching GitHub release asset.

## v0.1.0.0-AMC20260502.10 - 02.05.2026

### NSF Hang Mitigation Pass

- Moved NSF playback away from the worst blocking path by disabling automatic rendered-preview startup for NSF files.
- Routed NSF song playback through the imported trace sequencer as a temporary responsiveness fix.
- Reduced NSF trace import cost by tracing fewer subtunes, tightening wall-clock budgets, and stopping after repeated 6502 play-call timeouts.
- Changed NSF audio conversion to prefer the sequencer path where possible to avoid renderer lockups during export.
- Ran a 50-file NSF batch smoke test with audible output from all sampled files.
- Bumped the app informational version to `v0.1.0.0-AMC20260502.10`.
- Bumped `amChipper.SidPlayer.dll` and `amChipper.NsfPlayer.dll` to `v0.1.7.0`.
- Published the release asset `amChipper-v0.1.0.0-AMC20260502.10-win-x64.zip`.

## Unreleased - 02.05.2026

### NSF Responsiveness, Log Viewer Polish, and Localization

- Stopped running the SID/NSF preview renderer synchronously immediately after opening a chip file.
- Changed chip-file open to start preview rendering in the background, keeping the DAW responsive while the internal renderer warms a preview cache.
- Reduced NSF quick-preview cache length and preview sample rate so difficult NSF drivers cannot monopolize the machine as easily.
- Ran NSF preview rendering on a below-normal priority worker thread.
- Added instruction and wall-clock budgets to NSF init/play calls to prevent pathological NSF drivers from spinning too long inside the 6502 interpreter.
- Added a time budget to NSF tracker trace import and capped visible multi-subtune tracing so opening large NSF files does not hang the UI while still preserving source playback/render paths.
- Added first-pass 2A03 pulse sweep support so NSF pitch slides driven by pulse sweep registers are represented more accurately.
- Reworked the About-window log viewer from plain text into a styled, syntax-coloured log document with highlighted levels, prefixes, paths, and file names.
- Localized additional export and audio-conversion dialog strings.
- Corrected the standalone SID/NSF plugin facade version constants to match the current `v0.1.3.0` plugin assemblies.

## v0.1.0.0-AMC20260502.6 - 02.05.2026

### About Log Viewer, Localization, and Chip Playback

- Added a dedicated About-window Logs tab with a selectable tail view of the current `amChipper.log`.
- Added log-viewer controls for refreshing the log, copying the visible log tail, and opening the configured log folder.
- Localized the Help window tab headers, help-search empty-result message, About Logs tab, and log-viewer controls in English, German, and Norwegian.
- Routed the Windows title-bar system-menu About item through the live language layer instead of leaving it as hardcoded English.
- Added NSF APU frame-counter write handling for `$4017`, including five-step sequencing behavior and immediate frame updates when a tune switches modes.
- Routed NSF writes through `$4017` so drivers that depend on the frame-counter register no longer lose that state.
- Improved SID combined-noise waveform handling so noise mixed with saw/triangle/pulse produces a bounded approximation instead of dropping to silence.
- Bumped `amChipper.SidPlayer.dll` and `amChipper.NsfPlayer.dll` to `v0.1.3.0`.
- Bumped app informational version to `v0.1.0.0-AMC20260502.6`.

## v0.1.0.0-AMC20260502.5 - 02.05.2026

### NSF Playback Responsiveness and UI Coverage

- Changed SID/NSF song playback preview rendering to run asynchronously instead of blocking the WPF UI thread during Play.
- Added render-in-flight guarding so repeated Play clicks do not start multiple heavy chip preview renders.
- Kept the quick preview cache cap for chip playback while moving the expensive work off the transport click path.
- Added more translated bindings across the rack surface, including channel rack, automation rack, clip envelope, analyzer rack, instrument browser, instrument lab labels, spectrum labels, and common song-editor controls.
- Added pro-style rack header icons to channel rack, automation rack, clip envelope, instrument browser, sample browser, spectrum preview, and analyzer headings.
- Bumped `amChipper.SidPlayer.dll` and `amChipper.NsfPlayer.dll` to `v0.1.2.0`, still intentionally below `v1.0.0.0`.
- Bumped app informational version to `v0.1.0.0-AMC20260502.5`.

## v0.1.0.0-AMC20260502.4 - 02.05.2026

### NSF Stability, Localization, and Release Tools

- Reduced NSF play-call CPU budgets so unusually expensive NSF drivers cannot stall the UI as easily during render/trace playback.
- Capped first-play SID/NSF preview rendering to a 45-second quick cache so pressing Play does not synchronously render a whole long chip file before sound starts.
- Fixed the About credits scroller so it no longer fades the entire text stack to invisible forever; it now restarts from below the visible frame and uses the overlay fades only.
- Added missing language keys for newer settings controls, including piano-roll typing keyboard settings, analyzer mode, and Windows Open With registration.
- Localized the newly wired settings keys in English, German, and Norwegian, with exported packs regenerated for every bundled language.
- Added `amChipper.LanguageTool.exe` as the dedicated release tool for language pack export and validation.
- Stopped treating the full diagnostic console app as the user-facing language tool payload in `Ready2Release\tools`.
- Bumped `amChipper.SidPlayer.dll` and `amChipper.NsfPlayer.dll` to `v0.1.1.0`, keeping them below `v1.0.0.0` while chip accuracy is still being improved.
- Bumped app informational version to `v0.1.0.0-AMC20260502.4`.

## v0.1.0.0-AMC20260502.3 - 02.05.2026

### NSF / SID Deep Trace Pass

- Moved the actual SID/NSF/AMC support out of the Core build so the release now compiles those tracker/player entry points from the dedicated chip-support DLLs instead of embedding them in `amChipper.Core.dll`.
- Expanded NSF tracker imports from the previous 9 visible chip lanes to 28 lanes:
  - 2A03 pulse/triangle/noise/DPCM.
  - VRC6 pulse/saw.
  - VRC7 six-lane FM trace.
  - MMC5 pulse lanes.
  - Namco 163 eight wavetable lanes.
  - Sunsoft 5B three PSG lanes.
  - FDS wavetable lane.
- Fixed the lane-index mismatch where the emulator could snapshot VRC7/MMC5/N163/S5B/FDS voices but the importer discarded them because the tracker only allocated the old 9-lane map.
- Added SID voice-row inspection to the standalone `amChipper.SidPlayer.dll` facade so SID support can be diagnosed from the plugin boundary instead of only through app internals.
- Made `chip-batch` and `nsf-batch` stop after the requested limit without sorting/crawling the entire source tree first, making large folders such as `_EVERYTHING` usable for diagnostics.
- Extended chip batch output with imported tracker structure counts, including channels, patterns, playable notes, and effect rows.
- Validated the new path against bounded samples from the local NSF and HVSC folders; sampled files rendered audibly and imported into tracker-visible pattern structures.
- Bumped app informational version to `v0.1.0.0-AMC20260502.3`.

## v0.1.0.0-AMC20260502.2 - 02.05.2026

### Startup Crash Repair

- Fixed the startup crash caused by WPF trying to write back into the read-only localization indexer used by bindings like `{Binding [Open]}`.
- The localization indexer now accepts write-back attempts as no-ops, keeping computed translations read-only in practice while satisfying WPF controls whose default binding mode is writable.

### Translation Packs and Tooling

- Added editable JSON language packs under a release `lang` folder.
- The app now creates built-in language JSON files on startup if the `lang` folder is missing.
- Added custom language loading from `lang/*.json`, with bad custom packs ignored so one broken translation cannot stop the DAW from launching.
- Added `amChipper.Console lang-export` for exporting built-in language packs to any folder.
- Added `amChipper.Console lang-check` for validating pack entry counts and missing/extra translation keys.
- Added a `lang/README.txt` describing how translators should copy a pack, keep keys stable, and translate values.

### Updater and Chip Libraries

- Added a dependency-free `UpdateService` that reads a JSON update manifest, compares AMC build codes, and opens the download URL for newer builds.
- Added `amChipper.SidPlayer.dll` as the first standalone SID playback/import facade, versioned from `v0.1.0.0`.
- Added `amChipper.NsfPlayer.dll` as the first standalone NSF playback/import/trace facade, versioned from `v0.1.0.0`.
- Kept `amChipper.AmcPlayer.dll` as the standalone AMC player library and ensured SID/NSF/AMC player DLLs are publishable into the release `libs` directory.
- Bumped app informational version to `v0.1.0.0-AMC20260502.2`.

## v0.1.0.0-AMC20260502.1 - 02.05.2026

### Localization and About Window Polish

- Routed more visible shell text through the live language layer instead of leaving English strings hardcoded.
- Localized the Analyzer view menu item and the status-bar BPM / rows-per-beat labels.
- Localized the Project Hub empty/ready/dirty/restart/workflow status labels so language switching refreshes those computed strings live.
- Localized the About window changelog tab title, changelog intro, AMC tab title, and credits-scroller labels.
- Changed the About credits scroller so it starts fully below the visible frame instead of beginning half-way through the text.
- Added opacity and top/bottom edge fades to the About credits scroller so the text enters and leaves cleanly.

### NSF / SID Diagnostics and Expansion Audio

- Added the `chip-batch` console diagnostic for bounded SID/NSF corpus scanning across large chiptune folders.
- The diagnostic reports file type, peak/RMS audio, SID clock/model metadata, NSF song/start indexes, NSF expansion flags, audible/silent/fail counts, and per-file status.
- Extended the first-pass NSF renderer with approximation channels for additional expansion chips:
  - VRC7 FM register routing and six rough sine-based FM lanes.
  - MMC5 pulse/PCM routing.
  - Namco 163 wavetable RAM/register routing with up to eight traced voices.
  - Sunsoft 5B PSG register routing with three square-wave voices.
- Added the new expansion voices to NSF trace snapshots so imported NSF files can expose more tracker lanes when those chips are active.
- Expanded the CPU/APU write path so expansion-chip register writes are not swallowed as ordinary memory writes.
- Bumped assembly informational version to `v0.1.0.0-AMC20260502.1`.

## v0.1.0.0-AMC20260501.6 - 01.05.2026

### NSF Expansion Audio Pass

- Added a first-pass FDS wavetable channel to the internal NSF renderer.
- Routed FDS `$4040-$409F` register writes through the CPU/APU path instead of dropping them as ordinary memory writes.
- Mixed FDS wavetable output into the NSF stereo renderer.
- Added FDS state snapshots to the NSF trace system.
- Added an `FDS Wavetable` instrument/track lane to NSF imports so FDS activity is visible in the tracker, channel rack, and piano roll.
- Updated NSF trace import tests for the expanded nine-lane chip layout.
- Bumped assembly informational version to `v0.1.0.0-AMC20260501.6`.

## v0.1.0.0-AMC20260501.5 - 01.05.2026

### NSF Tracker Trace Import

- Replaced the placeholder NSF import that created only one empty `NSF Program` lane.
- NSF files now import as tracker-visible chip lanes:
  - `2A03 Pulse 1`
  - `2A03 Pulse 2`
  - `2A03 Triangle`
  - `2A03 Noise`
  - `2A03 DPCM`
  - `VRC6 Pulse 1`
  - `VRC6 Pulse 2`
  - `VRC6 Saw`
- Added NSF driver-state tracing from the running init/play routine into `NsfVoiceRow` records.
- Built imported NSF phrase patterns from live APU/VRC6 state so the playlist, channel rack, piano roll, and tracker editor have real rows to show.
- Added note, volume, raw timer/effect, and source metadata mapping for traced NSF rows.
- Added deterministic fallback trace rows for NSF files whose driver or expansion chip cannot yet produce usable live state.
- Updated the headless console dashboard so `.nsf`/`.nsfe` files import through the chip path instead of being incorrectly sent to libopenmpt.
- Added a regression test proving NSF import creates visible APU trace rows, eight chip lanes, pattern blocks, and playable notes.
- Bumped assembly informational version to `v0.1.0.0-AMC20260501.5`.

## v0.1.0.0-AMC20260501.4 - 01.05.2026

### NSF Playback Repair

- Replaced the fake digest-style NSF fallback as the normal path with a real first-pass NSF runtime.
- Added NSF header execution support for load, init, play, song count, start song, PAL/NTSC play-rate timing, and initial bank tables.
- Added 6502 init/play execution for NSF drivers, including official opcodes plus common unofficial opcodes used by real NES music drivers:
  - multi-byte NOPs
  - LAX/SAX
  - DCP/ISB
  - SLO/RLA/SRE/RRA
- Fixed NSF bank switching alignment so banked files respect the NSF load-address page offset instead of mapping every bank as if the payload started at `$8000`.
- Added NES RAM mirroring and `$4015` APU status reads so drivers that poll channel state no longer read dead memory.
- Reworked the 2A03 APU path with frame-sequencer ticking, pulse/noise envelope decay, length counters, triangle length handling, status reporting, and cleaner DPCM output.
- Removed the disabled-DPCM DC leak that made silent/broken NSF files look falsely audible in diagnostics.
- Added first-pass VRC6 expansion-chip register routing and rendering for VRC6 pulse/saw channels.
- Added the `nsf-batch` console diagnostic for running the internal NSF renderer against a file or directory and reporting audible/silent/failing files with peak/RMS stats.
- Tested the included `NSF` folder with the new diagnostic:
  - `180` files scanned
  - `151` produced measurable audio
  - `29` were now correctly flagged as silent instead of being hidden by fake DC output
  - `0` crashed or threw
- Added a regression NSF fixture that executes a real tiny NSF init/play program and writes APU registers instead of relying on random bytes.
- Current known NSF gaps:
  - FDS, N163, VRC7, MMC5, and Sunsoft 5B need dedicated expansion emulation for accurate playback.
  - Some silent files now represent real unsupported-driver/unsupported-expansion cases rather than the old fake-output path.

### Version Alignment

- Bumped the app, core, audio, console, and helper assembly informational version to `v0.1.0.0-AMC20260501.4`.

## v0.1.0.0-AMC20260501.3 - 01.05.2026

### Professional Instrument Editing

- Added an `Instrument Lab` below the instrument/sample browser so imported and scratch instruments can be edited from the main workspace.
- Added editable source and synth controls:
  - source type
  - waveform
  - root note
  - fine tune
  - pulse width
- Added envelope controls for volume-envelope enable, attack, hold, decay, sustain, and release.
- Added function controls for mono mode, portamento, polyphony cap, LFO amount, LFO speed, arpeggio range, and arpeggio repeat.
- Added chip-FX controls for echo feedback, echo time, filter cutoff, and filter resonance.
- The new instrument fields are part of the song model and are serialized through the amChipper project/custom format path.
- Native synth playback now applies instrument fine tuning and instrument LFO pitch movement.
- Instrument edits force editable/native playback so the edited sound path is used after changing instrument settings.
- Pattern and piano-roll views refresh after instrument edits so note previews and tracker views follow the edited instrument state.

### About Window and Credits

- Reworked the About window credits area into an animated oldschool scrolling credits panel inspired by classic DAW/about screens.
- Added explicit product credit for main coder and product direction: Geir Gustavsen.
- Added company credit: ZeroLinez Softworx.
- Added runtime and format credits into the scrolling text instead of burying them in a plain paragraph.
- Replaced the flat Changelog text view with expandable release/section groups.
- Changelog entries can now be expanded per version and per changed area, making line-level detail easier to inspect inside the app.
- The About Changelog tab now behaves like an interactive implementation history instead of a passive text dump.

### Source and Release Cleanup

- Cleaned new instrument, About, and configuration code paths with XML summaries where public-facing declarations were added.
- Fixed new instrument editor byte clamping so the Release build compiles cleanly on .NET 10.
- Fixed the instrument-edit refresh path to call the actual tracker row refresh API.
- Added a `Ready2Release` publish layout as the expected release drop folder in the repository root.
- Fixed the publish pipeline so dependency DLLs are moved into `Ready2Release\libs` instead of staying beside the executable.
- Strengthened the runtime dependency loader with both legacy `AssemblyResolve` and .NET `AssemblyLoadContext` resolution from the `libs` directory.
- Verified the release layout keeps `amChipper.exe` and the main app assembly at the root while `amChipper.Core`, `amChipper.Audio`, `amChipper.AmcPlayer`, NAudio, and QuickLog DLLs live in `libs`.
- Fixed the About Changelog parser so release expanders are rendered after all Markdown sections/items are attached; version groups now show real change counts instead of `0 changes`.
- Added a themed scrollbar template so scroll tracks, arrows, and thumbs use the active amChipper theme colors instead of default Windows gray.
- Added DWM title-bar color integration so the main window, Help window, and About window caption bars follow the active theme where supported by Windows.
- Added a themed slider template so clip volume/pan, mixer, settings, and instrument-lab slider controls no longer use default Windows styling.
- Added persisted main workspace splitter width for the left instrument/browser pane.
- Added a per-user `Register Open With` settings action that registers amChipper for supported tracker, chip, MIDI, FSC, AMC, and project file extensions without requiring administrator rights.
- Fixed the published app startup crash caused by a duplicate implicit `Slider` style in the theme dictionary.
- Added publish-time `.deps.json` normalization so dependency runtime paths point at `libs/*.dll`, matching the release folder layout before app code runs.
- Verified `Ready2Release\amChipper.exe` starts and remains running from the published folder.

### Native `.amc` Format Upgrade

- Reworked the unshipped first-generation `.amc` container while keeping the public magic/version as `AMCHIPMOD1`.
- `.amc` now preserves embedded original module payloads for imported tracker sources.
- The embedded source payload is stored as a separate compressed binary section instead of being thrown away.
- Loading a source-preserving `.amc` now restores the original embedded source format, extension, and module bytes while the app UI identifies the file as an amChipper AMC container.
- Reopened `.amc` files made from XM/MOD can use the same exact module playback and patch path as the original imported module.
- Fixed the major flaw where `.amc` forced every saved module to plain `AmChip` and lost original XM/MOD playback/effect fidelity.
- Regenerated `examples\Outlive no2.amc` from `Outlive no2.xm` using the upgraded container.
- The regenerated example is smaller than the source XM while retaining the embedded XM payload:
  - `Outlive no2.xm`: 31,410 bytes
  - `examples\Outlive no2.amc`: 25,604 bytes
- Added console dashboard support for `.amc` files with embedded source modules.
- Added a regression test proving `.amc` preserves embedded source data, original format metadata, and tracker effect bytes.
- Added an About-window AMC Format tab describing custom-format features.
- Project Hub and transport labels now describe `.amc` as an amChipper AMC container with embedded source details instead of misreporting only the embedded XM/MOD format.
- Aligned `amChipper.Core.dll`, `amChipper.Audio.dll`, console, and helper DLL assembly metadata with the main app version: `v0.1.0.0-AMC20260501.3`.
- Added `amChipper.AmcPlayer.dll`, a small standalone AMC playback library that references only `amChipper.Core` and renders normalized `.amc` song data to stereo float PCM without NAudio or libopenmpt.
- Improved piano-roll note interaction: right-click now opens a stable note context menu with audition, velocity, length, transpose, duplicate, and delete actions; Ctrl+right-click remains a quick delete shortcut.
- Added FL Studio-style piano-roll typing-keyboard preview with configurable enable/base-note/velocity controls in the piano-roll toolbar and Settings.
- Piano keyboard mouse preview now captures the mouse so held notes continue until the button is released, even if the cursor drifts off the key strip.

### AMC Format Feature Notes

- `.amc` is designed as a hybrid native container: exact source wrapper plus editable amChipper song model.
- It supports the amChipper song model's higher channel headroom, currently up to 64 channels.
- It can preserve tracker rows with note, instrument, volume column, raw effect command, and effect parameter data.
- It stores playlist blocks, tracks, instruments, samples, order list, timing, restart order, and automation-ready data.
- It allows amChipper-only features to grow beyond classic MOD/XM/S3M/IT restrictions while keeping embedded-source playback fidelity where available.

## v0.1.0.0-AMC20260501.2 - 01.05.2026

### Documentation and In-App Reference

- Added this detailed `CHANGELOG.md` covering the work from 30.04.2026 through 01.05.2026.
- Added a dedicated Changelog tab to the About window.
- The About Changelog tab reads the bundled `CHANGELOG.md` at runtime.
- Added a development fallback so the About window can also read the changelog from the repository root during local runs.
- Added `USER_GUIDE.txt` as a plain-text usage guide for opening, editing, playback, export, configuration, logs, and troubleshooting.
- Added XML `<summary>` documentation across source declarations.
- Verified declaration summary coverage for the targeted source pattern as `1984/1984 documented, 0 missing`.

### Example Content

- Added a native amChipper `.amc` example export based on `Outlive no2.xm`.
- The example is intended to demonstrate the custom amChipper native chip module path.
- The `.amc` file uses amChipper's Brotli-compressed native module container and loads through the regular Open dialog.

### Help and About

- Improved the Help window into a more useful in-app manual:
  - icon-led Feature Guide tab
  - Shortcuts tab
  - searchable full reference tab
- Added more icons to the main workspace tabs.
- Added more language choices:
  - Portuguese: `Português`
  - Finnish: `Suomi`
  - Czech: `Čeština`
  - Japanese: `日本語`
- Added shell/help/tip translation coverage for the new languages.

## v0.1.0.0-AMC20260430.18 - 30.04.2026

### Window Menu and Icon Pass

- Added `About amChipper...` to the real Windows title-bar system menu.
- The system menu entry is available from the title-bar icon and Alt+Space menu.
- Added Win32 system-menu integration through the WPF window handle.
- Added icons/glyphs to the main menu:
  - File commands
  - Edit commands
  - Song playback commands
  - View navigation
  - Help commands
- Added icons/glyphs to Project Hub quick actions.
- Added icons/glyphs to configuration actions.
- Added icons/glyphs to analyzer switching.
- Added icons/glyphs to piano-roll FX apply/clear.
- Added icons/glyphs to tracker editor clear-cell action.

## v0.1.0.0-AMC20260430.17 - 30.04.2026

### Analyzer Rack

- Added a dedicated Analyzer tab.
- Added a larger professional spectrum view.
- Added analyzer mode selection.
- Added master peak readout.
- Added source/timing/output info panels.
- Added analyzer entry to the View menu.
- Added analyzer mode to Settings.
- Connected the compact playlist spectrum to analyzer mode cycling.
- Clicking the compact playlist spectrum now opens the Analyzer tab.
- Fixed compact spectrum bar sizing so playlist toolbar bars are scaled for the small preview.

### Help Improvements

- Replaced the plain help text view with a tabbed help surface.
- Added a Feature Guide tab.
- Added a full reference tab.
- Improved the help presentation so it behaves more like a built-in manual.

### Layout Fixes

- Fixed piano-roll tracker FX rack alignment.
- Reworked the FX rack into clearer left/right panels.
- Widened the Project Hub workflow status column.
- Added safe trimming to workflow status values.
- Fixed About/Format/Runtime list selection colors so selected rows remain readable.

## v0.1.0.0-AMC20260430.16 - 30.04.2026

### Spectrum Analysis

- Reworked mixer spectrum analysis from simple waveform chunk bars to log-spaced frequency analysis.
- Added separate behaviour for `Studio Analyzer`, `Compact Bars`, and `Peak Focus`.
- Smoothed analyzer attack and release.
- Adjusted bar colors and glow intensity for better readability.
- Added compact, mixer, and analyzer-specific bar height properties.

### Highlight Polish

- Reduced heavy tab glow.
- Replaced selected-tab glare with a cleaner accent-line style.
- Improved selected piano-roll note styling.
- Reduced harsh selected-note white edges.
- Added softer selected-note glow and more controlled gloss.

## Earlier 30.04.2026 Work

### Project Identity, Splash, and App Chrome

- Added `icon_clean.png` as the WPF window icon.
- Added `icon.ico` as the executable icon.
- Added splash/logo usage based on bundled assets.
- Added splash progress/status behaviour.
- Added dependency-load display during startup.
- Removed the forced long splash delay after feedback.
- Added support for moving dependency DLLs into `libs`.
- Added runtime dependency resolution for the moved DLL layout.

### Themes and Visual Styling

- Added full-app theme switching.
- Added multiple visible themes:
  - FL Grape
  - Neon Studio
  - Classic Tracker
  - Amber CRT
  - Midnight Pro
  - Ice Matrix
  - Magenta Circuit
  - Carbon Lime
  - Ruby Wave
  - Ocean Lab
  - Steel Mono
  - Sunset Pop
- Fixed a crash caused by trying to mutate frozen WPF brush resources.
- Improved menu and popup colors for dark themes.
- Added UI shine, panel shadow, density, tooltip delay, tooltip duration, and help text scale settings.

### About Window

- Replaced the original message-box style About screen with a proper About window.
- Added tabs for:
  - Overview
  - Format Support
  - Runtime
- Added animated oldschool logo effects.
- Added About settings to enable/disable oldschool logo effects.
- Added credits for:
  - code/UI/tracker workflow
  - graphics assets
  - audio runtime
  - tracker/chip formats
- Added runtime/plugin dependency view.
- Filtered out noisy Microsoft/System/WPF assemblies from the plugin list.
- Added a format support table with extension, playback path, and export/edit behaviour.

### Configuration and Settings

- Added persistent configuration under `%LOCALAPPDATA%\amChipper\settings.json`.
- Added Save Config, Load Config, Export Config, Import Config, and Reset To Defaults.
- Added `.amchipsettings` configuration exchange.
- Added auto-save configuration on exit.
- Added log-directory configuration.
- Added verbose logging toggle.
- Added dependency-load logging toggle.
- Added audio output settings:
  - output device
  - sample rate
  - latency
  - buffer count
- Added module conversion settings:
  - SID-to-XM mode
  - chip render tail seconds
  - song-length database usage
  - native export warning behaviour

### Language and Tips

- Added live language switching.
- Added language choices in native display names.
- Added translations for major app shell sections, settings labels, help/tip surfaces, and common controls.
- Added startup tips.
- Added a Tip of the Day window.
- Fixed tips window button text.
- Disabled minimize/maximize on non-main windows where appropriate.

### Project Hub

- Added an editable Project Hub front page.
- Added editable song title and artist metadata.
- Added project structure cards.
- Added quick actions:
  - Open
  - Save
  - Export
  - Play/Pause
  - New Song
  - Add Track
  - Add Pattern
  - Add Instrument
  - Playlist
  - Piano Roll
  - Tracker Editor
  - Feature Reference
- Added project state and workflow summaries.

### Transport and Playback

- Added playback scopes:
  - Song
  - Pattern
  - Piano Roll
- Added keyboard shortcuts:
  - Space for play/pause
  - Shift+Space for stop
  - Ctrl+1 for song scope
  - Ctrl+2 for pattern scope
  - Ctrl+3 for piano-roll scope
- Added restart-order handling.
- Added option to start from tracker restart order.
- Added option to reset Stop to restart order when available.
- Improved transport readouts:
  - live BPM
  - initial BPM
  - speed
  - elapsed/total time
  - source format
  - playback state

### Playlist / Song Editor

- Added FL Studio-like playlist terminology.
- Added draw/select/erase tools.
- Added zoom controls.
- Added zoom reset and fit behaviour.
- Added mouse-wheel zoom support.
- Added visible playhead improvements.
- Improved track/channel rendering for modules with more than eight channels.
- Improved block sizing and timeline width behaviour.
- Added compact playlist spectrum preview.
- Added clip volume/pan controls.
- Added automation envelope area below the playlist.

### Piano Roll

- Added pattern and channel selection.
- Added current pattern label.
- Added piano-roll scoped playback.
- Added visual playhead for piano-roll playback.
- Added note drawing, erasing, moving, and velocity editing.
- Added right-click note deletion.
- Added double-click/context options for notes.
- Added note preview on piano key click and note mouse-down.
- Added support for playing the currently selected channel's instrument.
- Added tracker FX rack below the piano roll.
- Added raw XM command, parameter, and volume-command editing.
- Preserved tracker effect rows during piano-roll commit operations.
- Added logging around piano-roll commit and effect preservation.
- Added MIDI and FSC import/export paths for piano-roll note exchange.
- Added multi-pattern and multi-channel MIDI export support.

### Tracker Editor and Effects

- Added tracker-style row/channel editor.
- Added pattern navigation.
- Added pattern duplication.
- Added current cell effect label.
- Added raw tracker columns:
  - note
  - instrument
  - volume
  - effect
  - parameter
- Added effect preservation fixes during edits.
- Added more diagnostics for changed patterns/cells.

### Mixer, Channel Rack, and Automation

- Added channel rack population from loaded modules.
- Added channel effect summaries.
- Added mixer strips for channels.
- Added master volume and master meter.
- Added per-channel volume, pan, mute, and solo state.
- Added live VU-style readouts.
- Added automation rack surface.
- Added clip envelope volume/pan editing.
- Added detailed mixer readout setting.
- Added visualizer intensity and peak-hold settings.

### Formats, Import, Export, and Custom Project Files

- Added compressed amChipper project format `.amchip`.
- Added amChipper native chip module format `.amc`.
- Added `.amc` Brotli-compressed native module container.
- Added open-dialog support for `.amc`, `.amchip`, tracker modules, chip files, MIDI, and FSC.
- Added unified Export To flow.
- Added native source module export where exact export is possible.
- Added dirty native export warnings.
- Added XM export/conversion.
- Added WAV/MP3 audio conversion options.
- Added FSC import/export improvements.
- Added MIDI import/export improvements.

### Tracker Module Support

- Expanded tracker module support through the module catalog and libopenmpt routing.
- Primary formats include:
  - XM
  - MOD
  - S3M
  - IT
- Added broader known tracker/chiptune module extension routing for OpenMPT-compatible formats.
- Added structure extraction:
  - orders
  - patterns
  - rows
  - channels
  - instruments
  - samples
  - restart order when available
- Added native/original playback path when available.
- Added editable conversion path for supported structures.

### SID / NSF / Chip Work

- Added SID/NSF chip source import path.
- Added PSID/RSID metadata parsing:
  - title
  - artist
  - release
  - load/init/play addresses
  - song count
  - start song
  - clock/model metadata
- Added HVSC `Songlengths.txt` support.
- Added fallback duration lookup by SID filename.
- Fixed fallback behaviour where every SID appeared as exactly 3 minutes.
- Added SID trace generation into editable patterns.
- Added SID voice/filter lanes.
- Added SID-to-XM export modes:
  - Rendered Mix Only
  - Rendered Mix + Trace
  - Trace Only
- Added rendered SID mix sample-lane export for more faithful XM playback.
- Added trace sanitisation for XM-safe output.
- Added chip render tail configuration.
- Known limitation: exact editable SID-to-XM conversion is not fully equivalent to native SID playback because SID filters, oscillator interaction, timing tricks, and CPU-driven player code do not map cleanly to tracker rows.

### Headless Console Diagnostics

- Added console dashboard mode.
- Added self-test mode for `Outlive no2.xm`.
- Added SID-to-XM test mode.
- Added module summary output:
  - transport
  - channel rack
  - pattern grid
  - mixer
- Added peak checks for original modules, edited playback, pattern playback, piano-roll playback, and exported XM output.
- Added effect-diff and patch-diff reporting.
- Added AMC export command for generating custom native module examples.

### Logging

- Added verbose logging options.
- Added log-directory settings.
- Added dependency-load logging.
- Added detailed playback/edit/export logging points.
- Added piano-roll commit and effect-preservation logging.
- Added module import/export diagnostics.

### Validation

- Repeated Release builds were run during the work.
- Core test suite was repeatedly executed.
- Formatting verification was run after major passes.
- The latest validation for this changelog update is listed in the final task response.

## Known Gaps

- Exact SID-to-XM editable conversion is still inherently approximate.
- Some very old tracker/chip formats depend on libopenmpt/native compatibility rather than hand-written exact parsers.
- UI polish is ongoing; the app is moving toward an FL Studio-like chiptune tracker but is still evolving.
- The `.amc` format is amChipper-native and intended for amChipper playback/editing rather than third-party tracker compatibility.
