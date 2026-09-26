[CmdletBinding()]
param(
    [string]$SimConnectNativePath = "",
    [switch]$PreserveState
)

$ErrorActionPreference = "Stop"

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "The OpenCareer KJFK live-test profile requires Windows x64."
}

if (Get-Process -Name "OpenCareer.App" -ErrorAction SilentlyContinue) {
    throw "Close every running OpenCareer window before starting or resetting the isolated KJFK live-test profile."
}

if ([string]::IsNullOrWhiteSpace($SimConnectNativePath) -and -not [string]::IsNullOrWhiteSpace($env:MSFS2024_SDK)) {
    $SimConnectNativePath = Join-Path $env:MSFS2024_SDK "SimConnect SDK\lib\SimConnect.dll"
}

if ([string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
    throw "SimConnectNativePath is required when MSFS2024_SDK is not configured."
}

$SimConnectNativePath = [System.IO.Path]::GetFullPath($SimConnectNativePath)
if (-not (Test-Path -LiteralPath $SimConnectNativePath -PathType Leaf)) {
    throw "SimConnect.dll was not found at: $SimConnectNativePath"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\OpenCareer.App\OpenCareer.App.csproj"
$profileRoot = Join-Path $env:LOCALAPPDATA "OpenCareer.LiveTests\KJFK"

$dotnetArgs = @(
    "run",
    "--project", $project,
    "--configuration", "Release",
    "-p:Platform=x64",
    "-p:SimConnectNativePath=$SimConnectNativePath",
    "--",
    "--development-kjfk-live-test"
)

if (-not $PreserveState) {
    $dotnetArgs += "--reset-development-kjfk-live-test"
}

$mode = if ($PreserveState) { "PRESERVE/RECOVERY" } else { "CLEAN RESET" }
Write-Host "OpenCareer DEVELOPMENT / TEST — KJFK ($mode)"
Write-Host "Isolated data root: $profileRoot"
Write-Host "Normal data root is not opened or reset."

& dotnet @dotnetArgs
exit $LASTEXITCODE
