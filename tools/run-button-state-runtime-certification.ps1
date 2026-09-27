param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64")]
    [string]$Platform = "x64",

    [int]$TimeoutSeconds = 60,

    [switch]$Interactive
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\OpenCareer.App\OpenCareer.App.csproj"
$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) "OpenCareer-button-state-runtime"
$publishRoot = Join-Path $artifactRoot "app"
$reportPath = Join-Path $artifactRoot "button-state-runtime-report.json"

if (Test-Path $artifactRoot) {
    Remove-Item $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $publishRoot `
    -p:Platform=$Platform `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    throw "OpenCareer WinUI certification publish failed with exit code $LASTEXITCODE."
}

$appPath = Join-Path $publishRoot "OpenCareer.App.exe"
if (-not (Test-Path $appPath)) {
    throw "Compiled OpenCareer apphost was not found at $appPath."
}

$arguments = @(
    "--dev-button-state-runtime-probe",
    "--button-state-probe-output",
    "`"$reportPath`""
)

if (-not $Interactive) {
    $arguments += "--button-state-probe-auto-close"
}

$process = Start-Process `
    -FilePath $appPath `
    -ArgumentList $arguments `
    -WorkingDirectory $publishRoot `
    -PassThru

if ($Interactive) {
    Write-Host "OpenCareer DEVELOPMENT / TEST button-state visual probe launched."
    Write-Host "Use pointer hover/press, Tab focus, and the Visual state toggle."
    Write-Host "Runtime report: $reportPath"
    return
}

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    throw "OpenCareer WinUI runtime certification exceeded $TimeoutSeconds seconds."
}

$process.Refresh()
if ($process.ExitCode -ne 0) {
    if (Test-Path $reportPath) {
        Get-Content $reportPath | Write-Host
    }

    throw "OpenCareer WinUI runtime certification failed with exit code $($process.ExitCode)."
}

if (-not (Test-Path $reportPath)) {
    throw "OpenCareer WinUI runtime certification did not create its report."
}

$report = Get-Content $reportPath -Raw | ConvertFrom-Json
if ($report.schemaVersion -ne 1 -or
    -not $report.success -or
    -not $report.variantResourcesIsolated -or
    -not $report.stockTemplatePreserved -or
    -not $report.systemFocusBehaviorPreserved -or
    -not $report.nativeButtonAutomationPreserved) {
    Get-Content $reportPath | Write-Host
    throw "OpenCareer WinUI runtime certification report did not pass."
}

if ($report.variants.Count -ne 3) {
    throw "OpenCareer WinUI runtime certification did not verify all three variants."
}

foreach ($variant in $report.variants) {
    if ($variant.states.Count -ne 4) {
        throw "Variant $($variant.variant) did not certify all four visual states."
    }
}

Get-Content $reportPath | Write-Host
Write-Host "OpenCareer compiled WinUI button-state runtime certification passed."
