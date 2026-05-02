# Building

## Prerequisites

- Windows x64.
- .NET 10 SDK.
- Visual Studio with WPF/.NET desktop workload, or command-line .NET SDK.

## Restore, build, and test

```powershell
dotnet restore .\amChipper.sln
dotnet build .\amChipper.sln -c Release -p:Platform=x64
dotnet test .\amChipper.sln -c Release -p:Platform=x64 --no-build
```

## Publish

```powershell
dotnet publish .\src\amChipper.App\amChipper.App.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o .\Ready2Release
```

The published layout keeps `amChipper.exe` at the root and dependency DLLs under `libs/`. The build target updates the dependency map so double-click launching works from `Ready2Release`.

## Smoke test

After publish:

```powershell
.\Ready2Release\amChipper.exe
```

If the executable exits before the WPF window appears, run:

```powershell
dotnet .\Ready2Release\amChipper.dll
```

That usually exposes XAML parse errors, dependency-map problems, or startup exceptions.
