# amChipper Installer

This folder contains the amSetup project used to build the Windows installer
for amChipper. The project is maintained by `build\Build-AmChipperInstaller.ps1`;
edit the script for repeatable installer behavior and keep this folder as the
generated setup definition.

Build from the repository root:

```powershell
.\build\Build-AmChipperInstaller.ps1
```

For a quick package from the existing `Ready2Release` folder:

```powershell
.\build\Build-AmChipperInstaller.ps1 -SkipRestore -SkipBuild -SkipTests -SkipPublish -SkipAppSmokeTest
```

The output is written to:

```text
artifacts\installers\amChipper-<version>-win-x64-Setup.exe
```

The installer includes the published app, `libs`, language packs, examples,
documentation, the language tool, amChipper Open With registrations,
user-level `AMCHIPPER_HOME`, selectable desktop/start-menu/install-folder
shortcuts, an uninstaller, and .NET Desktop Runtime 10 prerequisite checks.

Useful options:

```powershell
.\build\Build-AmChipperInstaller.ps1 -InstallerTheme dark
.\build\Build-AmChipperInstaller.ps1 -InstallerTheme oldschool -Layout Split -ChunkSize 256m
.\build\Build-AmChipperInstaller.ps1 -RunInstallerSmokeTest
```
