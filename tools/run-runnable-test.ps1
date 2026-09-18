[CmdletBinding()]
param(
    [string]$SimConnectNativePath = "",
    [string]$ProbeOutput = "",
    [ValidateRange(0, 86400)]
    [int]$ProbeDurationSeconds = 0,
    [switch]$LaunchApp,
    [switch]$LaunchLiveProbe
)

$ErrorActionPreference = "Stop"

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "OpenCareer runnable validation requires Windows x64."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "== OpenCareer runnable validation ==" -ForegroundColor Cyan
    Write-Host "Repository: $repoRoot"

    if ([string]::IsNullOrWhiteSpace($SimConnectNativePath) -and -not [string]::IsNullOrWhiteSpace($env:MSFS2024_SDK)) {
        $candidate = Join-Path $env:MSFS2024_SDK "SimConnect SDK\lib\SimConnect.dll"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $SimConnectNativePath = $candidate
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
        $SimConnectNativePath = [System.IO.Path]::GetFullPath($SimConnectNativePath)
        if (-not (Test-Path -LiteralPath $SimConnectNativePath -PathType Leaf)) {
            throw "SimConnect.dll was not found at: $SimConnectNativePath"
        }

        Write-Host "SimConnect runtime: $SimConnectNativePath" -ForegroundColor DarkGray
    }
    else {
        Write-Host "SimConnect runtime not supplied; disconnected app build/test remains valid." -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "[1/4] Restore WinUI app" -ForegroundColor Cyan
    & dotnet restore "src/OpenCareer.App/OpenCareer.App.csproj" "-p:Platform=x64"
    if ($LASTEXITCODE -ne 0) { throw "WinUI restore failed with exit code $LASTEXITCODE." }

    Write-Host ""
    Write-Host "[2/4] Build WinUI app" -ForegroundColor Cyan
    $buildArgs = @(
        "build",
        "src/OpenCareer.App/OpenCareer.App.csproj",
        "--configuration", "Release",
        "--no-restore",
        "-p:Platform=x64"
    )
    if (-not [string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
        $buildArgs += "-p:SimConnectNativePath=$SimConnectNativePath"
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "WinUI build failed with exit code $LASTEXITCODE." }

    Write-Host ""
    Write-Host "[3/4] Build live SimConnect probe" -ForegroundColor Cyan
    $probeBuildArgs = @(
        "build",
        "tools/OpenCareer.LiveProbe/OpenCareer.LiveProbe.csproj",
        "--configuration", "Release",
        "-p:Platform=x64"
    )
    if (-not [string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
        $probeBuildArgs += "-p:SimConnectNativePath=$SimConnectNativePath"
    }
    & dotnet @probeBuildArgs
    if ($LASTEXITCODE -ne 0) { throw "Live probe build failed with exit code $LASTEXITCODE." }

    Write-Host ""
    Write-Host "[4/4] Run integrated xUnit suite" -ForegroundColor Cyan
    & dotnet test "tests/OpenCareer.Tests/OpenCareer.Tests.csproj" --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed with exit code $LASTEXITCODE." }

    Write-Host ""
    Write-Host "Automated runnable validation passed." -ForegroundColor Green

    if ($LaunchApp) {
        Write-Host "Launching OpenCareer App..." -ForegroundColor Cyan
        $appRunArgs = @(
            "run",
            "--project", "src/OpenCareer.App/OpenCareer.App.csproj",
            "--configuration", "Release",
            "-p:Platform=x64"
        )
        if (-not [string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
            $appRunArgs += "-p:SimConnectNativePath=$SimConnectNativePath"
        }

        Start-Process -FilePath "dotnet" -ArgumentList $appRunArgs -WorkingDirectory $repoRoot
    }

    if ($LaunchLiveProbe) {
        if ([string]::IsNullOrWhiteSpace($SimConnectNativePath)) {
            throw "-LaunchLiveProbe requires -SimConnectNativePath or the MSFS2024_SDK environment variable."
        }

        $liveProbeScript = Join-Path $repoRoot "tools\run-live-probe.ps1"
        $probeArgs = @{
            SimConnectNativePath = $SimConnectNativePath
            DurationSeconds = $ProbeDurationSeconds
        }
        if (-not [string]::IsNullOrWhiteSpace($ProbeOutput)) {
            $probeArgs.Output = $ProbeOutput
        }

        & $liveProbeScript @probeArgs
        if ($LASTEXITCODE -ne 0) { throw "Live SimConnect probe failed with exit code $LASTEXITCODE." }
    }
}
finally {
    Pop-Location
}
