# Bug Report, QuickLog, and Linux Release Plan

## Goal

Ship a small but useful support surface in the app, update the logging dependency to the latest QuickLog release, and publish a release path that produces Windows plus Linux-compatible artifacts.

## Repository status before work

- Local `main` and `origin/main` matched at `81711d30aab0e91f8d991c4b6d4924ff0b936c31`.
- Ahead/behind was `0/0`.
- QuickLog was a Git submodule under `src/QuickLog`.

## Completed tasks

1. Added `BugReportBuilder` in core with tested report body, `mailto:` URI, recipient, and log-tail behavior.
2. Added **Help -> Report Bug...** in the WPF app, addressed to `admin@darkmaster.no`.
3. Included current app version, status, project/file path, OS/runtime/architecture, and log tail in the generated report.
4. Added mail-client launch plus clipboard fallback.
5. Upgraded QuickLog to `v2.4.0` and pinned the submodule to `544e1e6`.
6. Adjusted the WPF app project so markup compilation resolves the QuickLog `net10.0` assembly.
7. Added a Linux Wine compatibility package because WPF itself remains Windows-only under .NET.
8. Updated the GitHub release workflow to build and upload `win-x64` and `linux-x64-wine` packages.
9. Updated README, changelog, release notes, and release process docs.

## Verification gates

1. Build the full solution in Release x64.
2. Run the full Release x64 test suite.
3. Run `dotnet format --verify-no-changes --exclude src/QuickLog` because `src/QuickLog` is an upstream submodule.
4. Publish the WPF app and language tool.
5. Export language packs.
6. Build local Windows and Linux Wine zip packages.
7. Smoke-test the published Windows app executable.
8. Commit, push `main`, then push tag `v0.2.4.0-AMC20260524.1`.

## Next work after this release

1. Decide whether Linux should stay Wine-compatible only or move toward a native Avalonia shell.
2. Add an optional HTTPS issue intake endpoint so bug reports can be submitted even when no local email client exists.
3. Add GitHub Actions Wine smoke checks once a reliable Linux runner recipe exists for the Windows Desktop Runtime inside Wine.
