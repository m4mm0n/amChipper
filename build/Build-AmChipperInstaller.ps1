# SPDX-License-Identifier: GPL-3.0-only

param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Platform = "x64",
    [string]$AmSetupRoot = "",
    [string]$PublishDir = "",
    [string]$OutputDir = "",
    [ValidateSet("Fastest", "Balanced", "Smallest", "Store")]
    [string]$Compression = "Balanced",
    [ValidateSet("Embedded", "External", "Split")]
    [string]$Layout = "Embedded",
    [ValidateSet("blue", "dark", "amber", "oldschool")]
    [string]$InstallerTheme = "dark",
    [string]$ChunkSize = "512m",
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$SkipAppSmokeTest,
    [switch]$SkipLibOpenMptDownload,
    [switch]$RebuildStub,
    [switch]$AllowFrameworkDependentStub,
    [switch]$FailOnAnalyzerWarnings,
    [switch]$RunInstallerSmokeTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = $RepoRoot
    )

    Write-Host ">> $FilePath $($ArgumentList -join ' ')"
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -NoNewWindow -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Command failed with exit code $($process.ExitCode): $FilePath"
    }
}

function Read-XmlProperty {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Fallback = ""
    )

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $node = $xml.Project.PropertyGroup |
        ForEach-Object { $_.$Name } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($node)) {
        return $Fallback
    }

    return [string]$node
}

function Copy-IfExists {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $pathFull = [IO.Path]::GetFullPath($Path)
    $rootUri = [Uri]::new($rootFull)
    $pathUri = [Uri]::new($pathFull)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function Add-RegistryValue {
    param(
        [Parameter(Mandatory = $true)]$List,
        [string]$Root = "HKCU",
        [Parameter(Mandatory = $true)][string]$Key,
        [string]$Name = "",
        [string]$Value = "",
        [string]$Kind = "String",
        [bool]$IgnoreFailure = $false
    )

    $List.Add([pscustomobject]@{
        os = "windows"
        root = $Root
        key = $Key
        name = $Name
        value = $Value
        valueKind = $Kind
        ignoreFailure = $IgnoreFailure
    }) | Out-Null
}

function Set-InstallerTheme {
    param($Manifest, [string]$Theme)

    switch ($Theme) {
        "blue" {
            $Manifest.theme.headerColor = "white"
            $Manifest.theme.accentColor = "blue"
            $Manifest.theme.progressColor = "cyan"
            $Manifest.theme.banner = "classic"
            $Manifest.window.style = "blue"
        }
        "amber" {
            $Manifest.theme.headerColor = "yellow"
            $Manifest.theme.accentColor = "yellow"
            $Manifest.theme.progressColor = "yellow"
            $Manifest.theme.banner = "classic"
            $Manifest.window.style = "amber"
        }
        "oldschool" {
            $Manifest.theme.headerColor = "white"
            $Manifest.theme.accentColor = "cyan"
            $Manifest.theme.progressColor = "green"
            $Manifest.theme.banner = "classic"
            $Manifest.window.style = "oldschool"
        }
        default {
            $Manifest.theme.headerColor = "gray"
            $Manifest.theme.accentColor = "cyan"
            $Manifest.theme.progressColor = "green"
            $Manifest.theme.banner = "minimal"
            $Manifest.window.style = "dark"
        }
    }
}

function Ensure-LibOpenMpt {
    param([Parameter(Mandatory = $true)][string]$Destination)

    $libs = Join-Path $Destination "libs"
    New-Item -ItemType Directory -Path $libs -Force | Out-Null
    if (Test-Path -LiteralPath (Join-Path $libs "libopenmpt.dll")) {
        return
    }

    $existing = Get-ChildItem -Path $RepoRoot -Recurse -File -Filter "libopenmpt.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notlike "*\Ready2Release\*" } |
        Select-Object -First 1
    if ($null -ne $existing) {
        foreach ($dll in Get-ChildItem -LiteralPath $existing.DirectoryName -File -Filter "*.dll") {
            Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $libs $dll.Name) -Force
        }
        return
    }

    if ($SkipLibOpenMptDownload) {
        throw "libopenmpt.dll is missing and -SkipLibOpenMptDownload was specified."
    }

    $url = "https://lib.openmpt.org/files/libopenmpt/dev/libopenmpt-0.8.6+release.dev.windows.vs2022.zip"
    $zipPath = Join-Path $env:TEMP ("amchipper-libopenmpt-" + [guid]::NewGuid().ToString("N") + ".zip")
    Write-Host ">> downloading libopenmpt runtime DLLs"
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UserAgent "amChipperInstallerBuilder/0.2"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -like "bin/amd64/*.dll" })
        if ($entries.Count -eq 0) {
            throw "No bin/amd64 DLLs were found in the libopenmpt archive."
        }

        foreach ($entry in $entries) {
            $out = Join-Path $libs $entry.Name
            if (Test-Path -LiteralPath $out) {
                Remove-Item -LiteralPath $out -Force
            }

            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $out)
        }
    }
    finally {
        $zip.Dispose()
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
    }
}

function Update-SetupFiles {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$InstallerPath,
        [Parameter(Mandatory = $true)][string]$PayloadRoot
    )

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $manifest.version = $Version
    $manifest.defaultInstallDirectory = "{ProgramFiles}\{Publisher}\{ProductName}"
    $licensePath = Join-Path $RepoRoot "LICENSE"
    if (Test-Path -LiteralPath $licensePath) {
        $license = Get-Content -LiteralPath $licensePath -Raw
        $termsEnd = $license.IndexOf("END OF TERMS AND CONDITIONS", [System.StringComparison]::OrdinalIgnoreCase)
        if ($termsEnd -ge 0) {
            $lineEnd = $license.IndexOf("`n", $termsEnd)
            if ($lineEnd -gt $termsEnd) {
                $license = $license.Substring(0, $lineEnd).TrimEnd()
            }
        }
        $manifest.licenseText = "amChipper is licensed under the GNU General Public License version 3 (GPLv3)." +
            [Environment]::NewLine + [Environment]::NewLine +
            "You must accept the GPLv3 license terms to install this build." +
            [Environment]::NewLine + [Environment]::NewLine +
            $license
    }
    $transparentSplash = Join-Path $RepoRoot "logo_splash_transparent.png"
    if (Test-Path -LiteralPath $transparentSplash) {
        $manifest.branding.splashPath = "..\logo_splash_transparent.png"
    }
    $manifest.window.width = 820
    $manifest.window.height = 560
    $manifest.window.introText = "Install the amChipper tracker DAW, native audio libraries, language packs, examples, documentation, and helper tools into the selected folder."
    Set-InstallerTheme -Manifest $manifest -Theme $InstallerTheme

    $debugComponent = @($manifest.components | Where-Object { $_.id -eq "debug-symbols" } | Select-Object -First 1)
    if ($debugComponent.Count -gt 0) {
        $pdbs = @(Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
            ForEach-Object { (Get-RelativePath -Root $PayloadRoot -Path $_.FullName).Replace('\', '/') } |
            Sort-Object)
        $debugComponent[0].include = @($pdbs)
    }

    $manifest.shortcuts = @(
        [pscustomobject]@{
            os = "windows"
            name = "amChipper"
            target = "{InstallDir}\amChipper.exe"
            arguments = ""
            location = "desktop"
            workingDirectory = "{InstallDir}"
        },
        [pscustomobject]@{
            os = "windows"
            name = "amChipper"
            target = "{InstallDir}\amChipper.exe"
            arguments = ""
            location = "startMenu"
            workingDirectory = "{InstallDir}"
        },
        [pscustomobject]@{
            os = "windows"
            name = "amChipper"
            target = "{InstallDir}\amChipper.exe"
            arguments = ""
            location = "install"
            workingDirectory = "{InstallDir}"
        }
    )

    $registry = [System.Collections.Generic.List[object]]::new()
    Add-RegistryValue -List $registry -Key "Software\ZeroLinez Softworx\amChipper" -Name "InstallDir" -Value "{InstallDir}"
    Add-RegistryValue -List $registry -Key "Software\ZeroLinez Softworx\amChipper" -Name "Version" -Value "{Version}"
    Add-RegistryValue -List $registry -Key "Software\Classes\ZeroLinez.amChipper.File" -Value "amChipper supported file"
    Add-RegistryValue -List $registry -Key "Software\Classes\ZeroLinez.amChipper.File" -Name "FriendlyTypeName" -Value "amChipper supported file"
    Add-RegistryValue -List $registry -Key "Software\Classes\ZeroLinez.amChipper.File\DefaultIcon" -Value "`"{InstallDir}\amChipper.exe`",0"
    Add-RegistryValue -List $registry -Key "Software\Classes\ZeroLinez.amChipper.File\shell\open\command" -Value "`"{InstallDir}\amChipper.exe`" `"%1`""
    Add-RegistryValue -List $registry -Key "Software\Classes\Applications\amChipper.exe" -Name "FriendlyAppName" -Value "amChipper"
    Add-RegistryValue -List $registry -Key "Software\Classes\Applications\amChipper.exe\shell\open\command" -Value "`"{InstallDir}\amChipper.exe`" `"%1`""

    $extensions = @(
        ".amchip", ".fsc", ".mid", ".midi",
        ".mod", ".xm", ".it", ".s3m", ".amc", ".mptm", ".669", ".abc", ".ahx", ".amf", ".ams",
        ".c67", ".dbm", ".digi", ".dmf", ".dsm", ".dtm", ".far", ".gdm", ".gtk", ".hvl", ".imf",
        ".ice", ".itp", ".j2b", ".m15", ".mdl", ".med", ".mgt", ".mms", ".mo3", ".mt2", ".mtm",
        ".nst", ".okt", ".plm", ".psm", ".ptm", ".pt36", ".sfx", ".sfx2", ".st26", ".stk", ".stm",
        ".stp", ".ult", ".umx", ".wow", ".xpk", ".sid", ".psid", ".rsid", ".nsf", ".nsfe"
    )
    foreach ($extension in $extensions | Sort-Object -Unique) {
        Add-RegistryValue -List $registry -Key "Software\Classes\$extension\OpenWithProgids" -Name "ZeroLinez.amChipper.File" -Kind "Binary" -Value ""
    }
    $manifest.registryValues = @($registry)

    $manifest | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

    $project = Get-Content -LiteralPath $ProjectPath -Raw | ConvertFrom-Json
    $project.payloadPath = "..\Ready2Release"
    $project.outputPath = "..\artifacts\installers\$(Split-Path -Leaf $InstallerPath)"
    $project.stubPath = "..\..\amSetup\artifacts\stubs\$RuntimeIdentifier\amSetup.exe"
    $project.compression = $Compression
    $project.layout = $Layout
    $project.chunkSize = $ChunkSize
    $project | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ProjectPath -Encoding UTF8
}

$RepoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$SolutionFile = Join-Path $RepoRoot "amChipper.sln"
$ProjectFile = Join-Path $RepoRoot "src\amChipper.App\amChipper.App.csproj"
$ToolProjectFile = Join-Path $RepoRoot "src\amChipper.LanguageTool\amChipper.LanguageTool.csproj"
$SetupDir = Join-Path $RepoRoot "setup"
$ManifestPath = Join-Path $SetupDir "amsetup.json"
$ProjectPath = Join-Path $SetupDir "amsetup.project.json"
$Version = Read-XmlProperty -Path (Join-Path $RepoRoot "Directory.Build.props") -Name "Version" -Fallback "0.1.0"
$InformationalVersion = Read-XmlProperty -Path (Join-Path $RepoRoot "Directory.Build.props") -Name "InformationalVersion" -Fallback $Version

if ([string]::IsNullOrWhiteSpace($AmSetupRoot)) {
    $AmSetupRoot = Resolve-FullPath (Join-Path $RepoRoot "..\amSetup")
}
else {
    $AmSetupRoot = Resolve-FullPath $AmSetupRoot
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $RepoRoot "Ready2Release"
}
else {
    $PublishDir = Resolve-FullPath $PublishDir
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "artifacts\installers"
}
else {
    $OutputDir = Resolve-FullPath $OutputDir
}

$AmSetupProject = Join-Path $AmSetupRoot "src\amSetup\amSetup.csproj"
$AmSetupBuildStubs = Join-Path $AmSetupRoot "build-stubs.ps1"
$StubPath = Join-Path $AmSetupRoot "artifacts\stubs\$RuntimeIdentifier\amSetup.exe"
$InstallerPath = Join-Path $OutputDir "amChipper-$Version-$RuntimeIdentifier-Setup.exe"
$AnalysisPath = Join-Path $OutputDir "amChipper-$Version-$RuntimeIdentifier-analysis.json"

if (-not (Test-Path -LiteralPath $AmSetupProject)) {
    throw "amSetup was not found at '$AmSetupRoot'. Pass -AmSetupRoot with the amSetup checkout path."
}

if ($RuntimeIdentifier -notlike "win-*") {
    throw "amChipper is a WPF desktop app, so this installer script targets Windows runtime identifiers only."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

if ($RebuildStub -or -not (Test-Path -LiteralPath $StubPath)) {
    if (-not (Test-Path -LiteralPath $AmSetupBuildStubs)) {
        throw "Cannot build amSetup stub because '$AmSetupBuildStubs' does not exist."
    }

    $iconPath = Join-Path $RepoRoot "icon.ico"
    $stubArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $AmSetupBuildStubs, "-Rids", $RuntimeIdentifier, "-NoAot")
    if (Test-Path -LiteralPath $iconPath) {
        $stubArgs += @("-IconPath", $iconPath)
    }

    Invoke-Checked -FilePath "powershell" -ArgumentList $stubArgs -WorkingDirectory $AmSetupRoot
}

if (-not (Test-Path -LiteralPath $StubPath)) {
    throw "amSetup stub was not found: $StubPath"
}

if (-not $SkipRestore) {
    Invoke-Checked -FilePath "dotnet" -ArgumentList @("restore", $SolutionFile) -WorkingDirectory $RepoRoot
}

if (-not $SkipBuild) {
    Invoke-Checked -FilePath "dotnet" -ArgumentList @("build", $SolutionFile, "-c", $Configuration, "-p:Platform=$Platform", "--no-restore") -WorkingDirectory $RepoRoot
}

if (-not $SkipTests) {
    Invoke-Checked -FilePath "dotnet" -ArgumentList @("test", $SolutionFile, "-c", $Configuration, "-p:Platform=$Platform", "--no-build") -WorkingDirectory $RepoRoot
}

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }

    Invoke-Checked -FilePath "dotnet" -ArgumentList @(
        "publish", $ProjectFile,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "false",
        "-p:Platform=$Platform",
        "-o", $PublishDir
    ) -WorkingDirectory $RepoRoot

    $toolsDir = Join-Path $PublishDir "tools"
    Invoke-Checked -FilePath "dotnet" -ArgumentList @(
        "publish", $ToolProjectFile,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "false",
        "-p:Platform=$Platform",
        "-o", $toolsDir
    ) -WorkingDirectory $RepoRoot

    $languageTool = Join-Path $toolsDir "amChipper.LanguageTool.exe"
    Invoke-Checked -FilePath $languageTool -ArgumentList @("export", "--output", (Join-Path $PublishDir "lang")) -WorkingDirectory $RepoRoot
}

Invoke-Checked -FilePath "powershell" -ArgumentList @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $RepoRoot "build\NormalizePublishDeps.ps1"),
    "-PublishDir", $PublishDir,
    "-AssemblyName", "amChipper"
) -WorkingDirectory $RepoRoot

Copy-IfExists -Source (Join-Path $RepoRoot "README.md") -Destination (Join-Path $PublishDir "README.md")
Copy-IfExists -Source (Join-Path $RepoRoot "RELEASE_NOTES.md") -Destination (Join-Path $PublishDir "RELEASE_NOTES.md")
Copy-IfExists -Source (Join-Path $RepoRoot "CHANGELOG.md") -Destination (Join-Path $PublishDir "CHANGELOG.md")
Copy-IfExists -Source (Join-Path $RepoRoot "LICENSE") -Destination (Join-Path $PublishDir "LICENSE")
Copy-IfExists -Source (Join-Path $RepoRoot "USER_GUIDE.txt") -Destination (Join-Path $PublishDir "USER_GUIDE.txt")

$examplesSource = Join-Path $RepoRoot "examples"
$examplesTarget = Join-Path $PublishDir "Examples"
if (Test-Path -LiteralPath $examplesSource) {
    New-Item -ItemType Directory -Path $examplesTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $examplesSource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $examplesTarget $_.Name) -Force
    }
}

Ensure-LibOpenMpt -Destination $PublishDir

$requiredPayloadFiles = @(
    "amChipper.exe",
    "amChipper.dll",
    "amChipper.deps.json",
    "amChipper.runtimeconfig.json",
    "libs\libopenmpt.dll",
    "libs\openmpt-mpg123.dll",
    "libs\openmpt-ogg.dll",
    "libs\openmpt-vorbis.dll",
    "libs\openmpt-zlib.dll",
    "tools\amChipper.LanguageTool.exe",
    "tools\amChipper.LanguageTool.dll",
    "lang\English.json",
    "USER_GUIDE.txt",
    "LICENSE"
)

foreach ($file in $requiredPayloadFiles) {
    $path = Join-Path $PublishDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Published payload is incomplete. Missing '$file' in '$PublishDir'."
    }
}

$depsText = Get-Content -LiteralPath (Join-Path $PublishDir "amChipper.deps.json") -Raw
if ($depsText -notmatch '"libs/') {
    throw "amChipper.deps.json does not reference libs/*.dll. The publish layout would not launch correctly."
}

if (-not $SkipAppSmokeTest) {
    $exe = Join-Path $PublishDir "amChipper.exe"
    Write-Host ">> smoke-testing published app startup"
    $process = Start-Process -FilePath $exe -WorkingDirectory $PublishDir -PassThru
    Start-Sleep -Seconds 5
    if ($process.HasExited) {
        throw "Published amChipper.exe exited early with code $($process.ExitCode). Run 'dotnet .\Ready2Release\amChipper.dll' for detailed WPF startup errors."
    }

    Stop-Process -Id $process.Id -Force
}

Update-SetupFiles -ManifestPath $ManifestPath -ProjectPath $ProjectPath -InstallerPath $InstallerPath -PayloadRoot $PublishDir

$analyzeArgs = @(
    "analyze",
    "--payload", $PublishDir,
    "--manifest", $ManifestPath,
    "--output", $AnalysisPath
)
Invoke-Checked -FilePath $StubPath -ArgumentList $analyzeArgs -WorkingDirectory $RepoRoot

if (Test-Path -LiteralPath $AnalysisPath) {
    $analysis = Get-Content -LiteralPath $AnalysisPath -Raw | ConvertFrom-Json
    $issues = @($analysis.issues)
    $errors = @($issues | Where-Object { $_.severity -eq "error" })
    $warnings = @($issues | Where-Object { $_.severity -eq "warning" })
    if ($errors.Count -gt 0) {
        $errors | ForEach-Object { Write-Error "$($_.code): $($_.message)" }
        throw "Dependency analysis found $($errors.Count) error(s)."
    }
    if ($FailOnAnalyzerWarnings -and $warnings.Count -gt 0) {
        $warnings | ForEach-Object { Write-Warning "$($_.code): $($_.message)" }
        throw "Dependency analysis found $($warnings.Count) warning(s)."
    }
}

$buildProjectArgs = @(
    "build-project",
    "--project", $ProjectPath
)
if ($AllowFrameworkDependentStub) {
    $buildProjectArgs += "--allow-framework-dependent-stub"
}

Invoke-Checked -FilePath $StubPath -ArgumentList $buildProjectArgs -WorkingDirectory $RepoRoot

if (-not (Test-Path -LiteralPath $InstallerPath)) {
    throw "Installer was not produced at '$InstallerPath'."
}

Invoke-Checked -FilePath $InstallerPath -ArgumentList @("inspect") -WorkingDirectory $RepoRoot
Invoke-Checked -FilePath $InstallerPath -ArgumentList @("--console", "--dry-run", "--silent") -WorkingDirectory $RepoRoot

if ($RunInstallerSmokeTest) {
    $smokeRoot = Join-Path $env:TEMP ("amchipper-installer-smoke-" + [guid]::NewGuid().ToString("N"))
    Invoke-Checked -FilePath $InstallerPath -ArgumentList @("--console", "--target", $smokeRoot, "--components", "main,examples,languages,tools,documentation", "--silent") -WorkingDirectory $RepoRoot
    if (-not (Test-Path -LiteralPath (Join-Path $smokeRoot "amChipper.exe"))) {
        throw "Installer smoke test did not install amChipper.exe."
    }
    Invoke-Checked -FilePath $InstallerPath -ArgumentList @("uninstall", "--target", $smokeRoot, "--silent") -WorkingDirectory $RepoRoot
}

$size = (Get-Item -LiteralPath $InstallerPath).Length
Write-Host ""
Write-Host "amChipper installer created:"
Write-Host "  $InstallerPath"
Write-Host ("  {0:N2} MB" -f ($size / 1MB))
Write-Host "  version: $Version"
Write-Host "  informational: $InformationalVersion"
Write-Host "  theme: $InstallerTheme"
Write-Host ""
Write-Host "Manual checks:"
Write-Host "  `"$InstallerPath`""
Write-Host "  `"$InstallerPath`" inspect"
Write-Host "  `"$InstallerPath`" --console --dry-run"
