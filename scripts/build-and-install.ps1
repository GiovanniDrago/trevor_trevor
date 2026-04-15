param(
    [string]$Configuration = "Release",
    [string]$GtaPath,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/TrevorTrevor/TrevorTrevor.csproj"
$requiredDlls = @(
    (Join-Path $repoRoot "lib/ScriptHookVDotNet3.3.7.0.104.dll")
)

foreach ($dll in $requiredDlls) {
    if (-not (Test-Path $dll)) {
        throw "Missing dependency: $dll"
    }
}

Write-Host "Building TrevorTrevor ($Configuration)..."
dotnet build $projectPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$builtDll = Join-Path $repoRoot "src/TrevorTrevor/bin/$Configuration/net48/TrevorTrevor.dll"
if (-not (Test-Path $builtDll)) {
    throw "Build completed but output not found: $builtDll"
}

if ($NoInstall) {
    Write-Host "Build succeeded. Output: $builtDll"
    exit 0
}

if ([string]::IsNullOrWhiteSpace($GtaPath)) {
    throw "-GtaPath is required unless -NoInstall is set."
}

$scriptsPath = Join-Path $GtaPath "scripts"
if (-not (Test-Path $scriptsPath)) {
    New-Item -ItemType Directory -Path $scriptsPath | Out-Null
}

$targetDll = Join-Path $scriptsPath "TrevorTrevor.dll"
Copy-Item -Path $builtDll -Destination $targetDll -Force

Write-Host "Installed to: $targetDll"
Write-Host "Done. Start GTA V and open TrevorTrevor menu in free roam."
