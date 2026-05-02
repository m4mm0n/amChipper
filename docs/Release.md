# Release Process

## Local validation

```powershell
dotnet restore .\amChipper.sln
dotnet build .\amChipper.sln -c Release -p:Platform=x64
dotnet test .\amChipper.sln -c Release -p:Platform=x64 --no-build
dotnet format .\amChipper.sln --verify-no-changes --no-restore
dotnet publish .\src\amChipper.App\amChipper.App.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o .\Ready2Release
.\Ready2Release\amChipper.exe
```

## Versioning

The maintainer controls version bumps. Do not update assembly or informational versions until a release is explicitly approved.

Current version format:

```text
v0.1.0.0-AMCyyyyMMdd.N
```

## GitHub release

1. Create a release build in `Ready2Release`.
2. Zip the publish folder.
3. Tag the validated commit.
4. Attach the zip and use `RELEASE_NOTES.md` as the starter notes.

GitHub CLI is optional. If it is installed:

```powershell
gh release create <tag> .\amChipper-<tag>-win-x64.zip --repo m4mm0n/amChipper --title "amChipper <tag>" --notes-file .\RELEASE_NOTES.md
```
