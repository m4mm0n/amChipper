@echo off
echo Cleaning amChipper build artifacts...

for %%d in (
    src\amChipper.App\obj
    src\amChipper.App\bin
    src\amChipper.Audio\obj
    src\amChipper.Audio\bin
    src\amChipper.Core\obj
    src\amChipper.Core\bin
) do (
    if exist "%%d" (
        echo   Removing %%d
        rd /s /q "%%d"
    )
)

echo Done. Open the solution in VS and do Build ^> Rebuild Solution.
pause
