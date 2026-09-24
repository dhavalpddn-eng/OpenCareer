param(
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [string]$RepositoryPath,
    [string]$BundlePath = "$env:USERPROFILE\Downloads\OpenCareer-p02-rapid-tests.bundle"
)

$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'OpenCareer.App' -ErrorAction SilentlyContinue) {
    throw 'Close OpenCareer before updating. Leave MSFS open if desired.'
}
if (-not $RepositoryPath) {
    $candidates = @(Get-ChildItem $env:USERPROFILE -Directory -Filter 'OpenCareer*' |
        Where-Object { Test-Path (Join-Path $_.FullName 'src\OpenCareer.App\OpenCareer.App.csproj') })
    if ($candidates.Count -ne 1) {
        $candidates.FullName | Write-Host
        throw 'Automatic discovery requires exactly one checkout. Run again with -RepositoryPath pointing to the desired checkout.'
    }
    $RepositoryPath = $candidates[0].FullName
}
Set-Location $RepositoryPath
git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Not a Git checkout.' }
$changes = git status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Checkout has local changes. Preserve them before updating.' }
if (-not (Test-Path $BundlePath)) { throw "Download the bundle first: $BundlePath" }
git bundle verify $BundlePath
if ($LASTEXITCODE -ne 0) { throw 'Bundle verification failed.' }
git fetch $BundlePath 'refs/heads/feature/p02-airframe-consequence'
if ($LASTEXITCODE -ne 0) { throw 'Bundle fetch failed.' }
git checkout --detach $ExpectedCommit
if ($LASTEXITCODE -ne 0) { throw 'Checkout failed.' }
if ((git rev-parse HEAD) -ne $ExpectedCommit) { throw 'Wrong commit.' }

$nativeDll = Get-ChildItem '.\src\OpenCareer.App\bin' -Filter SimConnect.dll -Recurse |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nativeDll) { throw 'Previous SimConnect.dll was not found. Stop here.' }

$filter = 'FullyQualifiedName~DevelopmentFlightTests|FullyQualifiedName~CareerJobBoardRefillServiceTests|FullyQualifiedName~JobAcceptanceFleetBridgeTests|FullyQualifiedName~AcceptedJobFlightSessionBridgeTests|FullyQualifiedName~CareerJobPlayableLoopCoordinatorTests|FullyQualifiedName~PlayerCareerExperienceCoordinatorTests|FullyQualifiedName~StandardPointToPointMissionCompletionSourceTests|FullyQualifiedName~FlightAirframeConsequenceTests'
dotnet test '.\tests\OpenCareer.Tests\OpenCareer.Tests.csproj' -c Release --filter $filter -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Focused tests failed. Stop here and retain the error output.' }
dotnet build '.\src\OpenCareer.App\OpenCareer.App.csproj' -c Release -p:Platform=x64 "-p:SimConnectNativePath=$($nativeDll.FullName)" -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Stop here and retain the error output.' }
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'Diff check failed.' }
Write-Host "BUILT COMMIT: $(git rev-parse HEAD)"
$exe = Join-Path $RepositoryPath 'src\OpenCareer.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\OpenCareer.App.exe'
if (-not (Test-Path $exe)) { throw "Executable not found: $exe" }
if (-not (Test-Path (Join-Path (Split-Path $exe) 'SimConnect.dll'))) { throw 'Native SimConnect.dll missing beside executable.' }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
