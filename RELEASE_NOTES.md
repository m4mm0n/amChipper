# amChipper v0.2.3.0-AMC20260503.6

This release applies the same corpus-driven hardening to SID playback that the previous build applied to NSF playback.

## Included

- Added a dedicated `sid-batch` diagnostic for large SID corpus validation without tracker-trace import overhead.
- Added `--sample-rate` and `--skip` support to batch diagnostics so large chip libraries can be validated in deterministic chunks.
- Added 6510 support for common unofficial SID-driver opcodes `$4B`/ALR and `$AB`/LAX immediate.
- Hardened the SID synth/filter/DC-block path so unstable filter states cannot leak `NaN` samples into playback.
- Tightened bad-frame throttling so pathological RSID play routines are deferred instead of burning the render loop.
- Inventoried 118,092 SID-like files under `C:\Users\admin\OneDrive\Dokumenter\My FTPRush Downloads\_EVERYTHING`.
- Post-fix corpus validation was stopped at 54,000 files by request after the remaining work became time-heavy.
- Validated result before stopping: 54,000 audible, 0 silent, 0 failed, 0 NaN outputs.
- App build bumped to `v0.2.3.0-AMC20260503.6`.
- SID plugin assembly and facade bumped to `v0.2.3.0`.
- NSF plugin assembly remains `v0.2.3.0`.

## Notes

SID chip-to-tracker reconstruction is still an approximation of driver state; this pass targets playback/render stability and finite audible output. The remaining unscanned SID range is not claimed as fully validated in this build.
