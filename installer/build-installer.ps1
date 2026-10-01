# My one step build: publish Kaydence as a single exe, then wrap it in an installer with Inno Setup
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Kaydence\Kaydence.csproj"
$publish = Join-Path $root "publish"

# I read the version from the project so the installer name always matches
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Building Kaydence $version"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "The publish step failed" }

# I look for Inno Setup wherever it usually installs itself
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 isn't installed. Get it free from https://jrsoftware.org/isdl.php and run this again."
    Write-Host "The finished app is already in $publish if I just want the exe."
    exit 1
}

& $iscc "/DMyAppVersion=$version" (Join-Path $PSScriptRoot "Kaydence.iss")
if ($LASTEXITCODE -ne 0) { throw "The installer step failed" }

$setup = Join-Path $PSScriptRoot "Output\Kaydence-Setup-$version.exe"
Write-Host ""
Write-Host "Done: $setup"
Write-Host "To release it, make a GitHub release tagged v$version and attach that file."
