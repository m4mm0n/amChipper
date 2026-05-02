param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$AssemblyName = "amChipper"
)

$PublishDir = $PublishDir.Trim('"', "'")
$AssemblyName = $AssemblyName.Trim('"', "'")
$depsPath = Join-Path $PublishDir "$AssemblyName.deps.json"
if (-not (Test-Path -LiteralPath $depsPath)) {
    return
}

$json = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$targetNames = @($json.targets.PSObject.Properties.Name)

foreach ($targetName in $targetNames) {
    $target = $json.targets.$targetName
    foreach ($libraryProperty in @($target.PSObject.Properties)) {
        $runtime = $libraryProperty.Value.runtime
        if ($null -eq $runtime) {
            continue
        }

        $runtimeProperties = @($runtime.PSObject.Properties)
        foreach ($runtimeProperty in $runtimeProperties) {
            $path = [string]$runtimeProperty.Name
            if (-not $path.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $fileName = [IO.Path]::GetFileName($path)
            if ($fileName.Equals("$AssemblyName.dll", [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $value = $runtimeProperty.Value
            $runtime.PSObject.Properties.Remove($path)
            $runtime | Add-Member -MemberType NoteProperty -Name "libs/$fileName" -Value $value
        }
    }
}

$json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $depsPath -Encoding UTF8
