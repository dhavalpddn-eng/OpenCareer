[CmdletBinding()]
param(
    [string]$SimConnectNativePath = "",
    [string]$Output = "",
    [ValidateRange(0, 86400)]
    [int]$DurationSeconds = 0,
    [switch]$FlightSession,
    [string]$Airport = ""
)

$ErrorActionPreference = "Stop"

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "OpenCareer live SimConnect validation requires Windows x64."
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
$project = Join-Path $repoRoot "tools\OpenCareer.LiveProbe\OpenCareer.LiveProbe.csproj"

$dotnetArgs = @(
    "run",
    "--project", $project,
    "--configuration", "Release",
    "-p:Platform=x64",
    "-p:SimConnectNativePath=$SimConnectNativePath",
    "--"
)

if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $dotnetArgs += @("--output", [System.IO.Path]::GetFullPath($Output))
}

if ($DurationSeconds -gt 0) {
    $dotnetArgs += @("--duration-seconds", $DurationSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if ($FlightSession) {
    $dotnetArgs += "--flight-session"
}

if (-not [string]::IsNullOrWhiteSpace($Airport)) {
    $dotnetArgs += @("--airport", $Airport.Trim())
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
