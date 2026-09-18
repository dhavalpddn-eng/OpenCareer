[CmdletBinding()]
param(
    [string]$SimConnectNativePath = "",
    [string]$Output = "",
    [ValidateRange(0, 86400)]
    [int]$DurationSeconds = 0,

    [string]$EscortContainerTitle = "",
    [string]$EscortFlightPlan = "",
    [string]$EscortLivery = "",
    [string]$EscortTailNumber = "OC001",
    [int]$EscortFlightNumber = -1,
    [ValidateRange(0, 1000000)]
    [double]$EscortFlightPlanPosition = 0.5,
    [switch]$EscortTouchAndGo
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

$hasEscortTitle = -not [string]::IsNullOrWhiteSpace($EscortContainerTitle)
$hasEscortPlan = -not [string]::IsNullOrWhiteSpace($EscortFlightPlan)
if ($hasEscortTitle -ne $hasEscortPlan) {
    throw "EscortContainerTitle and EscortFlightPlan must be supplied together."
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

if ($hasEscortTitle) {
    $dotnetArgs += @(
        "--escort-container-title", $EscortContainerTitle,
        "--escort-flight-plan", [System.IO.Path]::GetFullPath($EscortFlightPlan),
        "--escort-tail", $EscortTailNumber,
        "--escort-flight-number", $EscortFlightNumber.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--escort-position", $EscortFlightPlanPosition.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )

    if (-not [string]::IsNullOrWhiteSpace($EscortLivery)) {
        $dotnetArgs += @("--escort-livery", $EscortLivery)
    }

    if ($EscortTouchAndGo) {
        $dotnetArgs += "--escort-touch-and-go"
    }
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
