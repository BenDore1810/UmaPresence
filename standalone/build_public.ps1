param(
    [string]$CompilerPath,
    [string]$SdkLibraryPath,
    [string]$OutputDirectory,
    [switch]$PisaDev
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $CompilerPath) { $CompilerPath = Join-Path $projectRoot '.tools/zig/ziglang/zig.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'dist/public-native' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $projectRoot '.tools/zig-cache'
& (Join-Path $projectRoot 'bridge/build.ps1') -CompilerPath $CompilerPath -SdkLibraryPath $SdkLibraryPath -OutputDirectory $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Bridge compilation failed.' }
$sourceNames = @(
    'runtime.c','init-gate.c','cue.c','playback.c','formation.c','mix.c','routing.c','hooks.c','proxy.s',
    'upstream/minhook/src/buffer.c','upstream/minhook/src/hook.c','upstream/minhook/src/trampoline.c','upstream/minhook/src/hde/hde64.c'
)
$sources = $sourceNames | ForEach-Object { Join-Path $PSScriptRoot $_ }
$releaseDefine = if ($PisaDev) { '-DUMAPRESENCE_PISA_DEV=1' } else { '-DUMAPRESENCE_PUBLIC_RELEASE=1' }
& $CompilerPath cc -target x86_64-windows-gnu -O2 -fno-stack-protector $releaseDefine -shared @sources -lbcrypt -luser32 -ladvapi32 '-Wl,--strip-all' -o (Join-Path $OutputDirectory 'UnityPlayer.dll')
if ($LASTEXITCODE -ne 0) { throw 'Standalone runtime compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'generated/game-profile.json') -Destination $OutputDirectory -Force
$provenance = [ordered]@{
    standaloneSha256 = (Get-FileHash (Join-Path $OutputDirectory 'UnityPlayer.dll')).Hash.ToLowerInvariant()
    bridgeSha256 = (Get-FileHash (Join-Path $OutputDirectory 'umapyon_bridge.dll')).Hash.ToLowerInvariant()
    helperSha256 = 'f97609628ba41f32c7ffc8674894f2eea100a6bcf10f560da4fbd57b7a2a82cb'
    helperUrl = 'https://github.com/Hachimi-Hachimi/Cellar/releases/download/v2.0.0/cellar.dll'
    verifiedHachimiSha256 = '9fb42a1b4d150119a33b8de3480bd2876a8c3437734f8611fb09fa559631c70f'
    verifiedHachimiHelperSha256 = 'f97609628ba41f32c7ffc8674894f2eea100a6bcf10f560da4fbd57b7a2a82cb'
}
$provenance | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'loader-provenance.json') -Encoding UTF8
