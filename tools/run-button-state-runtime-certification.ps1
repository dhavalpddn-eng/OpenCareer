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
$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) "OpenCareer-button-state-runtime"
$reportPath = Join-Path $artifactRoot "button-state-runtime-report.json"
$stagePath = Join-Path $artifactRoot "button-state-runtime-stages.log"

if (Test-Path $artifactRoot) {
    Remove-Item $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$appRoot = Join-Path $repoRoot "src\OpenCareer.App\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$appPath = Join-Path $appRoot "OpenCareer.App.exe"
$dashboardXbfPath = Join-Path $appRoot "Views\DashboardPage.xbf"
$probeXbfPath = Join-Path $appRoot "Diagnostics\ButtonStateRuntimeProbePage.xbf"

$requiredArtifacts = @($appPath, $dashboardXbfPath, $probeXbfPath)
$invalidArtifacts = @(
    $requiredArtifacts | Where-Object {
        -not (Test-Path $_ -PathType Leaf) -or (Get-Item $_ -ErrorAction SilentlyContinue).Length -eq 0
    }
)

if ($invalidArtifacts.Count -gt 0) {
    throw "Compiled x64 $Configuration OpenCareer output is incomplete. Missing or empty required artifacts: $($invalidArtifacts -join ', ')."
}

Write-Host "OpenCareer compiled WinUI runtime root: $appRoot"
Write-Host "Dashboard XBF: $dashboardXbfPath"
Write-Host "Button-state probe XBF: $probeXbfPath"

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
    -WorkingDirectory $appRoot `
    -PassThru

if ($Interactive) {
    Write-Host "OpenCareer DEVELOPMENT / TEST button-state visual probe launched."
    Write-Host "Use pointer hover/press, Tab focus, and the Visual state toggle."
    Write-Host "Runtime report: $reportPath"
    return
}

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    if (Test-Path $stagePath) {
        Write-Host "OpenCareer runtime stage journal:"
        Get-Content $stagePath | Write-Host
    }

    throw "OpenCareer WinUI runtime certification exceeded $TimeoutSeconds seconds."
}

$process.Refresh()
if (Test-Path $stagePath) {
    Write-Host "OpenCareer runtime stage journal:"
    Get-Content $stagePath | Write-Host
}

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
