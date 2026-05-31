# amChipper v0.2.5.1

This hotfix tightens the v0.2.5 native-libopenmpt release around the reported playback, editor, analyzer, mixer, and AMC round-trip problems.

## Included

- Tracker Editor row auto-scroll now clips rows below the header so channel labels stay visible while playback follows the cursor.
- Piano Roll pattern changes preserve the selected channel instead of jumping back to the first populated lane.
- The restart-order transport option now loops from the restart order after module end; normal Play from the start seeks order 0.
- Analyzer modes now differ in window size, band grouping, dB floor, response curve, and peak hold.
- Mixer and Channel Rack meters now use smoothed attack/release with stronger color meters instead of flat, identical pulses.
- AMC exports keep the native amChipper song model and may carry a tracker playback cache so clean XM/MOD reloads can play back through libopenmpt with effect fidelity.
- App build bumped to `v0.2.5.1-AMC20260531.2`.

## Validation

- `dotnet build .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
- `dotnet test .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
