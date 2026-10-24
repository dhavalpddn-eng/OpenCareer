[CmdletBinding()]
param(
    [string]$SimConnectNativePath = "",
    [switch]$PreserveState
)

$ErrorActionPreference = "Stop"

$profileMarkerContents = "OpenCareer DEVELOPMENT / TEST profile: KJFK v1"
$buildConfiguration = "Release"
$buildPlatform = "x64"

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must not be a redirected path: $Path"
    }
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directorySeparators = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd($directorySeparators)
    $rootPrefix = $fullRoot + [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The diagnostic path is outside the fixed KJFK live-test root: $fullPath"
    }
}

function Assert-NoReparsePointsUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    Assert-NotReparsePoint -Path $Root -Description "KJFK live-test profile root"
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return
    }

    foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($Root)) {
        $attributes = [System.IO.File]::GetAttributes($entry)
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "KJFK live-test profile content must not be redirected: $entry"
        }

        if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
            Assert-NoReparsePointsUnderRoot -Root $entry
        }
    }
}

function Initialize-KjfkLiveTestProfileMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfileRoot
    )

    $profileParent = Split-Path -Parent $ProfileRoot
    $profileMarker = Join-Path $ProfileRoot ".opencareer-development-profile"

    Assert-NotReparsePoint -Path $profileParent -Description "KJFK live-test profile parent"
    Assert-NotReparsePoint -Path $ProfileRoot -Description "KJFK live-test profile root"
    [void][System.IO.Directory]::CreateDirectory($ProfileRoot)
    Assert-NotReparsePoint -Path $profileParent -Description "KJFK live-test profile parent"
    Assert-NotReparsePoint -Path $ProfileRoot -Description "KJFK live-test profile root"

    if (Test-Path -LiteralPath $profileMarker) {
        Assert-NotReparsePoint -Path $profileMarker -Description "KJFK live-test profile marker"
        if (-not (Test-Path -LiteralPath $profileMarker -PathType Leaf) -or
            -not [string]::Equals(
                [System.IO.File]::ReadAllText($profileMarker),
                $profileMarkerContents,
                [System.StringComparison]::Ordinal)) {
            throw "The fixed KJFK live-test profile marker is invalid; launch was refused."
        }

        return
    }

    if ([System.IO.Directory]::GetFileSystemEntries($ProfileRoot).Count -ne 0) {
        throw "The fixed KJFK live-test profile is not empty and has no valid profile marker."
    }

    $markerTemporary = Join-Path $ProfileRoot (
        ".profile-marker-{0}.tmp" -f [System.Guid]::NewGuid().ToString("N"))
    try {
        [System.IO.File]::WriteAllText(
            $markerTemporary,
            $profileMarkerContents,
            (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::Move($markerTemporary, $profileMarker)
    }
    finally {
        if (Test-Path -LiteralPath $markerTemporary -PathType Leaf) {
            [System.IO.File]::Delete($markerTemporary)
        }
    }
}

function Test-AllowlistedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    Assert-NotReparsePoint -Path $Path -Description "Diagnostic artifact"
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The allowlisted diagnostic artifact is not a file: $Path"
    }

    return $true
}

function Add-AllowlistedZipEntry {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $entry = $Archive.CreateEntry(
        $EntryName,
        [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $null
    $sourceStream = $null

    try {
        $entryStream = $entry.Open()
        $fileShare = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
        $sourceStream = [System.IO.File]::Open(
            $SourcePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            $fileShare)
        $sourceStream.CopyTo($entryStream)
    }
    finally {
        if ($null -ne $sourceStream) {
            $sourceStream.Dispose()
        }

        if ($null -ne $entryStream) {
            $entryStream.Dispose()
        }
    }
}

function Complete-KjfkLiveTestRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfileRoot,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [Parameter(Mandatory = $true)]
        [string]$RunMode,

        [Parameter(Mandatory = $true)]
        [string]$LaunchedAtUtc,

        [Parameter(Mandatory = $true)]
        [string]$SourceHead,

        [Parameter(Mandatory = $true)]
        [bool]$SourceDirty,

        [Parameter(Mandatory = $true)]
        [string]$BuildConfiguration,

        [Parameter(Mandatory = $true)]
        [string]$BuildPlatform,

        [Parameter(Mandatory = $true)]
        [int]$ExitCode,

        [string]$LaunchError = ""
    )

    if ($RunId -notmatch "^[0-9a-f]{32}$") {
        throw "The KJFK live-test run ID is not a lowercase N-format GUID."
    }

    $profileParent = Split-Path -Parent $ProfileRoot
    $profileMarker = Join-Path $ProfileRoot ".opencareer-development-profile"

    Assert-NotReparsePoint -Path $profileParent -Description "KJFK live-test profile parent"
    Assert-NotReparsePoint -Path $ProfileRoot -Description "KJFK live-test profile root"
    if (-not (Test-Path -LiteralPath $ProfileRoot -PathType Container)) {
        throw "The fixed KJFK live-test profile root was not created: $ProfileRoot"
    }

    Assert-NotReparsePoint -Path $profileMarker -Description "KJFK live-test profile marker"
    if (-not (Test-Path -LiteralPath $profileMarker -PathType Leaf) -or
        -not [string]::Equals(
            [System.IO.File]::ReadAllText($profileMarker),
            $profileMarkerContents,
            [System.StringComparison]::Ordinal)) {
        throw "The fixed KJFK live-test profile marker is missing or invalid; diagnostic finalization was refused."
    }

    $diagnosticsRoot = Join-Path $ProfileRoot "Diagnostics"
    Assert-PathWithinRoot -Path $diagnosticsRoot -Root $ProfileRoot
    Assert-NotReparsePoint -Path $diagnosticsRoot -Description "KJFK live-test diagnostics root"
    [void][System.IO.Directory]::CreateDirectory($diagnosticsRoot)
    Assert-NotReparsePoint -Path $diagnosticsRoot -Description "KJFK live-test diagnostics root"

    $runsRoot = Join-Path $diagnosticsRoot "KjfkLiveTest"
    Assert-PathWithinRoot -Path $runsRoot -Root $ProfileRoot
    Assert-NotReparsePoint -Path $runsRoot -Description "KJFK live-test run root"
    [void][System.IO.Directory]::CreateDirectory($runsRoot)
    Assert-NotReparsePoint -Path $runsRoot -Description "KJFK live-test run root"

    $runRoot = Join-Path $runsRoot $RunId
    Assert-PathWithinRoot -Path $runRoot -Root $ProfileRoot
    Assert-NotReparsePoint -Path $runRoot -Description "KJFK live-test run directory"
    [void][System.IO.Directory]::CreateDirectory($runRoot)
    Assert-NotReparsePoint -Path $runRoot -Description "KJFK live-test run directory"

    $manifestPath = Join-Path $runRoot "launch-manifest.json"
    $eventsPath = Join-Path $runRoot "state-events.jsonl"
    $summaryPath = Join-Path $runRoot "diagnostic-summary.json"
    $runResultPath = Join-Path $runRoot "run-result.json"
    $logRoot = Join-Path $ProfileRoot "Logs"
    $logPath = Join-Path $logRoot "opencareer.log"
    $previousLogPath = Join-Path $logRoot "opencareer.log.1"

    foreach ($allowlistedPath in @(
        $manifestPath,
        $eventsPath,
        $summaryPath,
        $runResultPath,
        $logPath,
        $previousLogPath)) {
        Assert-PathWithinRoot -Path $allowlistedPath -Root $ProfileRoot
    }

    $manifestExists = Test-AllowlistedFile -Path $manifestPath
    $eventsExist = Test-AllowlistedFile -Path $eventsPath
    $summaryExists = Test-AllowlistedFile -Path $summaryPath

    $manifestMatchesRun = $false
    if ($manifestExists) {
        try {
            $manifestDocument = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
            $manifestMatchesRun = [string]::Equals(
                [string]$manifestDocument.runId,
                ([System.Guid]::ParseExact($RunId, "N")).ToString("D"),
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $manifestMatchesRun = $false
        }
    }

    $summaryFinalized = $false
    if ($summaryExists) {
        try {
            $summaryDocument = [System.IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json
            $summaryFinalized = (
                [string]::Equals(
                    [string]$summaryDocument.runId,
                    ([System.Guid]::ParseExact($RunId, "N")).ToString("D"),
                    [System.StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals(
                    [string]$summaryDocument.outcome,
                    "NormalShutdown",
                    [System.StringComparison]::Ordinal) -and
                -not [string]::IsNullOrWhiteSpace([string]$summaryDocument.completedAtUtc))
        }
        catch {
            $summaryFinalized = $false
        }
    }

    $logExists = $false
    $previousLogExists = $false
    Assert-NotReparsePoint -Path $logRoot -Description "KJFK live-test log root"
    $logExists = Test-AllowlistedFile -Path $logPath
    $previousLogExists = Test-AllowlistedFile -Path $previousLogPath

    if (Test-Path -LiteralPath $runResultPath) {
        throw "The launcher-owned run result already exists: $runResultPath"
    }

    if ($ExitCode -ne 0) {
        $status = "Failed"
    }
    elseif ($manifestExists -and
        $manifestMatchesRun -and
        $eventsExist -and
        $summaryExists -and
        $summaryFinalized) {
        $status = "Succeeded"
    }
    else {
        $status = "DiagnosticsIncomplete"
    }
    $completedAtUtc = [System.DateTime]::UtcNow.ToString(
        "o",
        [System.Globalization.CultureInfo]::InvariantCulture)
    $runResult = [ordered]@{
        schemaVersion = 1
        runId = $RunId
        runMode = $RunMode
        launchedAtUtc = $LaunchedAtUtc
        completedAtUtc = $completedAtUtc
        status = $status
        exitCode = $ExitCode
        source = [ordered]@{
            head = $SourceHead
            dirty = $SourceDirty
        }
        profile = [ordered]@{
            name = "KjfkLiveTest"
            dataRoot = $ProfileRoot
            normalDataExcluded = $true
        }
        build = [ordered]@{
            configuration = $BuildConfiguration
            platform = $BuildPlatform
        }
        artifacts = [ordered]@{
            launchManifest = $manifestExists
            launchManifestMatchesRun = $manifestMatchesRun
            stateEvents = $eventsExist
            diagnosticSummary = $summaryExists
            diagnosticSummaryFinalized = $summaryFinalized
            currentLog = $logExists
            previousLog = $previousLogExists
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LaunchError)) {
        $runResult["launchError"] = $LaunchError
    }

    $runResultTemporary = Join-Path $runRoot (
        ".run-result-{0}.tmp" -f [System.Guid]::NewGuid().ToString("N"))
    try {
        $runResultJson = $runResult | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText(
            $runResultTemporary,
            $runResultJson,
            (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::Move($runResultTemporary, $runResultPath)
    }
    finally {
        if (Test-Path -LiteralPath $runResultTemporary -PathType Leaf) {
            [System.IO.File]::Delete($runResultTemporary)
        }
    }

    $zipName = "OpenCareer-kjfk-live-test-$RunId.zip"
    $zipPath = Join-Path $diagnosticsRoot $zipName
    if (Test-Path -LiteralPath $zipPath) {
        throw "The launcher-owned diagnostic ZIP already exists: $zipPath"
    }

    $zipTemporary = Join-Path $diagnosticsRoot (
        ".$zipName.{0}.tmp" -f [System.Guid]::NewGuid().ToString("N"))
    $zipStream = $null
    $archive = $null

    try {
        Add-Type -AssemblyName System.IO.Compression
        try {
            $zipStream = [System.IO.File]::Open(
                $zipTemporary,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            $archive = New-Object System.IO.Compression.ZipArchive(
                $zipStream,
                [System.IO.Compression.ZipArchiveMode]::Create,
                $false)

            if ($manifestExists) {
                Add-AllowlistedZipEntry $archive $manifestPath "launch-manifest.json"
            }
            if ($eventsExist) {
                Add-AllowlistedZipEntry $archive $eventsPath "state-events.jsonl"
            }
            if ($summaryExists) {
                Add-AllowlistedZipEntry $archive $summaryPath "diagnostic-summary.json"
            }

            Add-AllowlistedZipEntry $archive $runResultPath "run-result.json"

            if ($logExists) {
                Add-AllowlistedZipEntry $archive $logPath "logs/opencareer.log"
            }
            if ($previousLogExists) {
                Add-AllowlistedZipEntry $archive $previousLogPath "logs/opencareer.log.1"
            }
        }
        finally {
            if ($null -ne $archive) {
                $archive.Dispose()
                $archive = $null
            }

            if ($null -ne $zipStream) {
                $zipStream.Dispose()
                $zipStream = $null
            }
        }

        [System.IO.File]::Move($zipTemporary, $zipPath)
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }

        if ($null -ne $zipStream) {
            $zipStream.Dispose()
        }

        if (Test-Path -LiteralPath $zipTemporary -PathType Leaf) {
            [System.IO.File]::Delete($zipTemporary)
        }
    }

    Write-Host "Run result: $runResultPath"
    Write-Host "Diagnostic ZIP: $zipPath"

    return [pscustomobject]@{
        Status = $status
        RunResultPath = $runResultPath
        ZipPath = $zipPath
    }
}

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
$localApplicationData = [System.Environment]::GetFolderPath(
    [System.Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
    throw "The Windows LocalApplicationData folder could not be resolved."
}

$profileRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $localApplicationData "OpenCareer.LiveTests\KJFK"))

$sourceHeadOutput = @(& git -C $repoRoot rev-parse --verify HEAD 2>$null)
$sourceHeadExitCode = $LASTEXITCODE
$sourceHead = ($sourceHeadOutput -join "").Trim()
if ($sourceHeadExitCode -ne 0 -or $sourceHead -notmatch "^[0-9a-fA-F]{40}$") {
    throw "The exact Git HEAD could not be resolved for the KJFK live-test run."
}

$sourceStatus = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all --ignore-submodules=none 2>$null)
$sourceStatusExitCode = $LASTEXITCODE
if ($sourceStatusExitCode -ne 0) {
    throw "The Git working-tree state could not be resolved for the KJFK live-test run."
}

$sourceDirty = -not [string]::IsNullOrWhiteSpace(($sourceStatus -join "`n"))
$launchedAt = [System.DateTime]::UtcNow
$launchedAtUtc = $launchedAt.ToString(
    "o",
    [System.Globalization.CultureInfo]::InvariantCulture)
$runId = [System.Guid]::NewGuid().ToString("N")
$runMode = if ($PreserveState) { "PreserveRecovery" } else { "CleanReset" }

$processEnvironment = [ordered]@{
    OPENCAREER_KJFK_RUN_ID = $runId
    OPENCAREER_KJFK_SOURCE_HEAD = $sourceHead.ToLowerInvariant()
    OPENCAREER_KJFK_SOURCE_DIRTY = $sourceDirty.ToString().ToLowerInvariant()
    OPENCAREER_KJFK_LAUNCHED_AT_UTC = $launchedAtUtc
    OPENCAREER_KJFK_RUN_MODE = $runMode
    OPENCAREER_KJFK_BUILD_CONFIGURATION = $buildConfiguration
    OPENCAREER_KJFK_BUILD_PLATFORM = $buildPlatform
}

foreach ($environmentEntry in $processEnvironment.GetEnumerator()) {
    [System.Environment]::SetEnvironmentVariable(
        [string]$environmentEntry.Key,
        [string]$environmentEntry.Value,
        [System.EnvironmentVariableTarget]::Process)
}

Initialize-KjfkLiveTestProfileMarker -ProfileRoot $profileRoot

$logRootBeforeLaunch = Join-Path $profileRoot "Logs"
Assert-PathWithinRoot -Path $logRootBeforeLaunch -Root $profileRoot
Assert-NotReparsePoint -Path $logRootBeforeLaunch -Description "KJFK live-test log root"
Assert-NoReparsePointsUnderRoot -Root $profileRoot

$dotnetArgs = @(
    "run",
    "--project", $project,
    "--configuration", $buildConfiguration,
    "-p:Platform=$buildPlatform",
    "-p:SimConnectNativePath=$SimConnectNativePath",
    "--",
    "--development-kjfk-live-test"
)

if (-not $PreserveState) {
    $dotnetArgs += "--reset-development-kjfk-live-test"
}

$mode = if ($PreserveState) { "PRESERVE/RECOVERY" } else { "CLEAN RESET" }
Write-Host "OpenCareer DEVELOPMENT / TEST - KJFK ($mode)"
Write-Host "Isolated data root: $profileRoot"
Write-Host "Normal data root is not opened or reset."
Write-Host "Run ID: $runId"
Write-Host "Source: $sourceHead (dirty: $($sourceDirty.ToString().ToLowerInvariant()))"

$dotnetExitCode = 1
$launchError = ""
$finalizationFailed = $false
$finalizationResult = $null
$previousErrorActionPreference = $ErrorActionPreference
try {
    $dotnetCommand = Get-Command "dotnet" -CommandType Application -ErrorAction Stop
    $ErrorActionPreference = "Continue"
    & $dotnetCommand.Path @dotnetArgs
    if ($null -ne $LASTEXITCODE) {
        $dotnetExitCode = [int]$LASTEXITCODE
    }
}
catch {
    $launchError = $_.Exception.Message
    $dotnetExitCode = 1
    Write-Error "The KJFK live-test process could not be launched: $launchError" -ErrorAction Continue
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
    try {
        $finalizationResult = Complete-KjfkLiveTestRun `
            -ProfileRoot $profileRoot `
            -RunId $runId `
            -RunMode $runMode `
            -LaunchedAtUtc $launchedAtUtc `
            -SourceHead $sourceHead `
            -SourceDirty $sourceDirty `
            -BuildConfiguration $buildConfiguration `
            -BuildPlatform $buildPlatform `
            -ExitCode $dotnetExitCode `
            -LaunchError $launchError
    }
    catch {
        $finalizationFailed = $true
        Write-Error "KJFK live-test diagnostic finalization failed: $($_.Exception.Message)" -ErrorAction Continue
    }
}

$processExitCode = $dotnetExitCode
if ($processExitCode -eq 0 -and
    ($finalizationFailed -or
        $null -eq $finalizationResult -or
        $finalizationResult.Status -ne "Succeeded")) {
    $processExitCode = 2
}

exit $processExitCode
