# amChipper v0.2.5.0

This release moves amChipper back to the native original libopenmpt runtime and fixes several broken editing/playback surfaces before rebuilding the Windows and Linux Wine packages.

## Included

- Native libopenmpt release path restored with OpenMPT 0.8.7 runtime DLLs.
- Piano Roll tab no longer disrupts active playback or bounces the playhead between song and pattern coordinates.
- Piano-roll playback can be restarted from the first row with a repeat/replay command.
- Playlist blocks now draw compact note/effect previews that reflect the pattern lane data.
- `.amc` now exports real amChipper native song data instead of a hidden original-module wrapper.
- Analyzer now reports peak, RMS, dominant frequency, spectral centroid, stereo correlation, and clipping status.
- About/runtime view now reports QuickLog's own version instead of the amChipper build version.
- Settings import/reset and visible workspace/project settings now reapply their runtime effects.
- App build bumped to `v0.2.5.0-AMC20260531.1`.

## Validation

- `dotnet build .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
- `dotnet test .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
