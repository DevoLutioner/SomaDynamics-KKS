param(
    [string]$Version = "1.2.2",

    [string]$GameRoot = ''
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = $env:KKS_BUILD_GAME_ROOT
}
if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    $GameRoot = 'H:\KKS\KKS'
}
$GameRoot = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\')

$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $base "MmdDynamicBoneStabilizer.csproj"
# KKS-only build; the game root is passed as KKS_BUILD_GAME_ROOT.
$dll = Join-Path $base "bin\Release\MmdDynamicBoneStabilizer.dll"
$releaseRoot = Join-Path $base "release"
$stage = Join-Path $releaseRoot "MmdDynamicBoneStabilizer-KKS-v$Version"
$pluginDir = Join-Path $stage "BepInEx\plugins\MmdDynamicBoneStabilizer"
$zip = Join-Path $releaseRoot "MmdDynamicBoneStabilizer-KKS-v$Version.zip"

dotnet build $project -c Release "-p:KKS_BUILD_GAME_ROOT=$GameRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build output not found: $dll"
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir "MmdDynamicBoneStabilizer.dll") -Force
Copy-Item -LiteralPath (Join-Path $base "README.zh-CN.md") -Destination (Join-Path $stage "README.zh-CN.md") -Force
Copy-Item -LiteralPath (Join-Path $base "CHANGELOG.md") -Destination (Join-Path $stage "CHANGELOG.md") -Force

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + ".sha256") -Value ("$hash  " + (Split-Path -Leaf $zip)) -Encoding ascii
Write-Host "Package created: $zip"
Write-Host "SHA256: $hash"
