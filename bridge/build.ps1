param([switch]$Inspect, [string]$CompilerPath, [string]$SdkLibraryPath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = if ($CompilerPath) { $CompilerPath } else { Join-Path $projectRoot '.tools\zig\ziglang\zig.exe' }
$sdk = if ($SdkLibraryPath) { $SdkLibraryPath } else { Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Lib\10.0.22000.0\um\x64' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'dist' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $projectRoot '.tools\zig-cache'
$inspectArgs = @()
if ($Inspect) { $inspectArgs += '-DUMAPRESENCE_INSPECT=1' }
& $compiler cc -target x86_64-windows-msvc -O2 -fms-extensions -fno-stack-protector -fno-builtin -shared -nostdlib @inspectArgs (Join-Path $PSScriptRoot 'umapyon_bridge.c') (Join-Path $sdk 'kernel32.lib') (Join-Path $sdk 'ntdll.lib') '-Wl,-e,DllMain' -o (Join-Path $output 'umapyon_bridge.dll')
if ($LASTEXITCODE -ne 0) { throw 'Native bridge compilation failed.' }
Get-Item -LiteralPath (Join-Path $output 'umapyon_bridge.dll') | Select-Object Name,Length
