param([string]$GameDirectory, [switch]$FixturesOnly, [string]$CompilerPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not $FixturesOnly) {
        if(-not $GameDirectory){$GameDirectory=(Get-Content dist/standalone-preview/settings.json -Raw | ConvertFrom-Json).GameDirectory}
        $profile=Get-Content standalone/generated/game-profile.json -Raw | ConvertFrom-Json
        foreach($module in @(@{Name='UnityPlayer.dll';Hash=$profile.unity.sha256},@{Name='GameAssembly.dll';Hash=$profile.assembly.sha256})) {
            if((Get-FileHash -LiteralPath (Join-Path $GameDirectory $module.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $module.Hash){throw ('Game build mismatch: '+$module.Name)}
            Write-Output ('PASS: verified installed '+$module.Name)
        }
        $manifest=Get-Content standalone/upstream/hachimi-edge/source-manifest.json -Raw | ConvertFrom-Json
        foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path 'standalone/upstream/hachimi-edge' $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw ('Pinned source mismatch: '+$file.path)}}
        Write-Output ('PASS: '+$manifest.files.Count+' pinned upstream source/license hashes')
        $coverage=Get-Content standalone/generated/vocal-coverage.json -Raw | ConvertFrom-Json
        $data=Join-Path $GameDirectory 'umamusume_Data/Persistent/dat'
        foreach($row in $coverage.rows){
            $acb=Join-Path (Join-Path $data $row.acb.Substring(0,2)) $row.acb
            $awb=Join-Path (Join-Path $data $row.awb.Substring(0,2)) $row.awb
            if((Get-FileHash -LiteralPath $acb -Algorithm SHA256).Hash.ToLowerInvariant() -ne $row.acbSha256 -or (Get-Item -LiteralPath $awb).Length -ne $row.awbBytes){throw ('Cached recording changed: '+$row.song+'/'+$row.voice+'/'+$row.part)}
        }
        Write-Output ('PASS: '+$coverage.rows.Count+' local cue hashes and audio-file sizes; metadata evidence only')
    }
    $zig = if ($CompilerPath) { $CompilerPath } else { Join-Path $projectRoot '.tools/zig/ziglang/zig.exe' }
    New-Item -ItemType Directory -Force -Path dist | Out-Null
    if (-not $env:ZIG_GLOBAL_CACHE_DIR) { $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $projectRoot '.tools/zig-cache' }
    function Compile([string[]]$Sources,[string]$Output,[switch]$Shared){
        $arguments=@('cc','-target','x86_64-windows-gnu','-O2')
        if($Shared){$arguments+='-shared'}
        & $zig @arguments @Sources -o $Output
        if($LASTEXITCODE -ne 0){throw ('Native fixture compilation failed: '+$Output)}
    }
    function CheckExe([string]$Path,[string[]]$Arguments=@()) {& $Path @Arguments;if($LASTEXITCODE -ne 0){throw ('Fixture failed: '+$Path)}}
    foreach($test in @('playback-policy','playback-parameters','audio-policy','concert-regression','formation','routing','pe-identity')){
        Compile @("standalone/tests/$test-test.c") "dist/$test-test.exe"
        CheckExe "dist/$test-test.exe"
    }
    $minhook=@('buffer.c','hook.c','trampoline.c','hde/hde64.c') | ForEach-Object {'standalone/upstream/minhook/src/'+$_}
    Compile (@('standalone/tests/hooks-test.c','standalone/hooks.c')+$minhook) 'dist/standalone-hooks-test.exe'
    CheckExe 'dist/standalone-hooks-test.exe'
    New-Item -ItemType Directory -Force -Path dist/init-gate-fixture | Out-Null
    Compile @('standalone/tests/init-vm-fixture.c') 'dist/init-gate-fixture/GameAssembly.dll' -Shared
    Compile @('standalone/tests/init-unity-fixture.c') 'dist/init-gate-fixture/fake-unity.dll' -Shared
    Compile @('standalone/tests/init-gate-test.c','standalone/init-gate.c') 'dist/init-gate-test.exe'
    CheckExe 'dist/init-gate-test.exe';CheckExe 'dist/init-gate-test.exe' @('--utf16');CheckExe 'dist/init-gate-test.exe' @('--fail')
    if (-not $FixturesOnly) {
        & python -m unittest discover -s tools -p 'test_*.py'
        if ($LASTEXITCODE -ne 0) { throw 'Metadata parser regression failed' }
    }
    Write-Output 'OFFLINE CHECKS PASSED. This does not certify live game/audio behavior.'
} finally {Pop-Location}
