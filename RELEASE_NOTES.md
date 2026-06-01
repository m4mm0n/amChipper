# amChipper v0.2.5.2

This hotfix repairs the local `Ready2Release` startup package and prevents stale managed DLLs from being copied into the release `libs` folder again.

## Included

- Fixed the `Method not found: AudioEngine.get_ModulePlayer()` startup crash caused by stale `amChipper.Audio.dll` / `amChipper.Core.dll` files being copied into `Ready2Release\libs`.
- Restricted the local package builder so it copies only native OpenMPT runtime DLLs from existing OpenMPT folders instead of every DLL beside them.
- Added package validation for the managed `libs` DLL versions so stale Core/Audio/AMC/TrackerPlayer assemblies fail the build before release.
- Hardened MainWindow shutdown after early constructor failures so one startup failure does not cascade into a second null-reference dialog.
- App build bumped to `v0.2.5.2-AMC20260601.1`.

## Validation

- `dotnet build .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
- `dotnet test .\amChipper.sln -c Release -p:Platform=x64 -p:UseManagedLibOpenMpt=false`
- `.\build\Build-AmChipperInstaller.ps1 -SkipRestore -SkipBuild -SkipTests`
