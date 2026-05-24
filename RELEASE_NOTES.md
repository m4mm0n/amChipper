# amChipper v0.2.4.0-AMC20260524.1

This release adds a maintainer-facing bug-report flow, updates QuickLog to the latest v2.4.0 build, and publishes both Windows and Linux Wine packages.

## Included

- Added **Help -> Report Bug...** to prepare support emails to `admin@darkmaster.no`.
- Bug reports include user notes, reproduction fields, app version, current status/file context, runtime/OS details, and the current log tail.
- Added a clipboard fallback when the system mail client cannot open the generated report.
- Upgraded the QuickLog submodule to `v2.4.0` for the latest .NET 10/Linux-capable QuickLog release.
- Added a Linux Wine compatibility package with a launcher and setup notes.
- Updated the GitHub release workflow to build, test, package, and upload both `win-x64` and `linux-x64-wine` zip files.
- App build bumped to `v0.2.4.0-AMC20260524.1`.

## Linux note

WPF is still Windows-only under .NET. The Linux package runs the Windows WPF build through Wine and requires the Windows x64 .NET Desktop Runtime 10.x inside the Wine prefix. A native Linux UI remains a future Avalonia-style port rather than a WPF runtime switch.
