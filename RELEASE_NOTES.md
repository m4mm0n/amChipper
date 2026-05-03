# amChipper v0.2.2.0-AMC20260503.5

This release finishes the NSF corpus hardening pass by validating every `.nsf`/`.nsfe` file found in the local `_EVERYTHING` library and fixing the remaining live-playback rejection case.

## Included

- Fixed bank-switched NSF files that declare `Load=$0000` while relying on the initial bank table for `$8000-$FFFF` mapping.
- Kept invalid non-banked zero-load headers rejected instead of weakening general NSF validation.
- Added regression coverage for the banked zero-load streaming path.
- Validated the live NSF batch path against all 4,682 `.nsf`/`.nsfe` files found under `C:\Users\admin\OneDrive\Dokumenter\My FTPRush Downloads\_EVERYTHING`.
- Corpus result after the fix: 4,682 audible, 0 silent, 0 failed.
- App build bumped to `v0.2.2.0-AMC20260503.5`.
- SID plugin assembly bumped to `v0.2.2.0` because the shared chip renderer lives there.
- NSF plugin assembly bumped to `v0.2.3.0`.

## Notes

No `.nsfe` files were present in the scanned local corpus, but the existing NSFE normalization path remains covered by tests. NSF chip-to-tracker reconstruction is still an approximation of driver state; this pass targets live NSF playback/import stability across the available file corpus.
