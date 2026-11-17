param([Parameter(Mandatory=$true)][string]$CompilerPath, [Parameter(Mandatory=$true)][string]$SdkLibraryPath, [string]$OutputDirectory, [string]$HelperPath, [string]$CompatibilityFeedConfig)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$buildArgs = @{ PublicRelease=$true; OutputDirectory=(Join-Path $root 'dist/public-build') }
if ($CompatibilityFeedConfig) { $buildArgs.CompatibilityFeedConfig = $CompatibilityFeedConfig }
& (Join-Path $root 'build.ps1') @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Companion build failed' }
& (Join-Path $root 'standalone/build_public.ps1') -CompilerPath $CompilerPath -SdkLibraryPath $SdkLibraryPath
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
$tests = Join-Path $root 'dist/public-build/UmapyonPresence.Tests.exe'
$testArgs = @('--public-release')
if ($HelperPath) { $testArgs += [IO.Path]::GetFullPath($HelperPath) }
& $tests @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Public policy checks failed' }
if ($HelperPath) {
    $cache = Join-Path $root 'dist/public-build/dependencies'
    New-Item -ItemType Directory -Force $cache | Out-Null
    Copy-Item -LiteralPath $HelperPath -Destination (Join-Path $cache 'cellar-v2.0.0.dll') -Force
}
$policyTest = Join-Path $root 'dist/public-native-policy-test.exe'
& $CompilerPath cc -target x86_64-windows-gnu -O2 -DUMAPRESENCE_PUBLIC_RELEASE=1 (Join-Path $root 'standalone/tests/playback-parameters-test.c') -o $policyTest
if ($LASTEXITCODE -ne 0) { throw 'Native policy test compilation failed' }
$nativeOutput = @(& $policyTest)
if ($LASTEXITCODE -ne 0) { throw 'Native policy test failed' }
[ordered]@{adapterSha256=(Get-FileHash (Join-Path $root 'standalone/playback.c')).Hash.ToLowerInvariant(); testSourceSha256=(Get-FileHash (Join-Path $root 'standalone/tests/playback-parameters-test.c')).Hash.ToLowerInvariant(); testBinarySha256=(Get-FileHash $policyTest).Hash.ToLowerInvariant(); status='passed'; assertions=$nativeOutput} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'dist/public-native/public-policy-check.json') -Encoding UTF8
& python -m unittest discover -s $PSScriptRoot -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'Release gate tests failed' }
$arguments = @((Join-Path $PSScriptRoot 'package_public.py'), '--root', $root)
if ($OutputDirectory) { $arguments += @('--output', $OutputDirectory) }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Public package gate failed' }
