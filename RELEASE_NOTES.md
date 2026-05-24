# amChipper v0.2.4.0-AMC20260524.2

This hotfix keeps the new bug-report dialog usable while the app is running and QuickLog owns the live log file.

## Included

- Fixed **Help -> Report Bug...** so it no longer throws when `amChipper.log` is locked by the current process.
- The report now reads the log with shared access when possible.
- If the log file is still locked or unavailable, the report includes a clear note instead of crashing the dialog.
- Added regression coverage for locked log files.
- App build bumped to `v0.2.4.0-AMC20260524.2`.

## Still included from v0.2.4.0-AMC20260524.1

- Bug reports are addressed to `admin@darkmaster.no`.
- QuickLog is upgraded to `v2.4.0`.
- Release workflow publishes both Windows and Linux Wine packages.
