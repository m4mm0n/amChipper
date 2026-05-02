# Contributing to amChipper

amChipper is currently private and early, but the repository is prepared for proper open-source collaboration.

## Rules

- Keep changes focused and testable.
- Run the Release build and tests before submitting code.
- Do not add third-party music corpora, commercial score libraries, or assets unless their license is compatible with GPL-3.0-only and the file can be redistributed.
- Do not bump product/library versions unless the maintainer asks for a release/version update.
- Preserve the `libs/` dependency layout for WPF publish output.

## Contribution license

By contributing, you agree that your contribution is provided under the repository license and the additional maintainer relicensing grant in `CONTRIBUTOR_TERMS.md`.

That grant is what lets Geir Gustavsen / ZeroLinez Softworx keep the public version GPLv3 while still being able to publish proprietary or commercial editions later.

## Validation

```powershell
dotnet restore .\amChipper.sln
dotnet build .\amChipper.sln -c Release -p:Platform=x64
dotnet test .\amChipper.sln -c Release -p:Platform=x64 --no-build
dotnet format .\amChipper.sln --verify-no-changes --no-restore
```
