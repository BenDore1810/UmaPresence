param([switch]$TestsOnly, [string]$OutputDirectory, [switch]$PublicRelease, [string]$CompatibilityFeedConfig, [switch]$PisaDev)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'dist' }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$referenceDir = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5.2'
$sdkDir = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$winmd = Get-ChildItem -Path $sdkDir -Filter Windows.winmd -Recurse | Where-Object { $_.Directory.Name -match '^10\.' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $winmd) { throw 'Windows 10 SDK metadata is required to build (not to run).' }
$dotnetDir = Join-Path $env:ProgramFiles 'dotnet'
$compiler = Get-ChildItem -Path (Join-Path $dotnetDir 'sdk') -Filter csc.dll -Recurse | Where-Object { $_.FullName -match 'Roslyn\\bincore' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $compiler) { throw '.NET SDK is required to build (not to run).' }
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Runtime.WindowsRuntime.dll', 'System.IO.Compression.dll') | ForEach-Object { '/r:' + (Join-Path $frameworkDir $_) }
$references += Get-ChildItem -LiteralPath (Join-Path $referenceDir 'Facades') -Filter *.dll | ForEach-Object { '/r:' + $_.FullName }
$references += '/r:' + $winmd.FullName
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter *.cs | ForEach-Object FullName
$commonArgs = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:latest', '/optimize+', '/platform:x64', '/utf8output', '/warn:4') + $references + $sources
if ($PublicRelease) { $commonArgs += @('/deterministic+', ('/pathmap:' + $projectRoot + '=/_/umapresence')) }
if ($PublicRelease) { $commonArgs += '/define:PUBLIC_RELEASE' }
if ($PisaDev) {
    if (-not $PublicRelease) { throw 'Use -PublicRelease with -PisaDev to retain the portable installer.' }
    $commonArgs += '/define:PISA_DEV'
}
if ($CompatibilityFeedConfig) {
    $feedPath = (Resolve-Path -LiteralPath $CompatibilityFeedConfig).Path
    if ((Get-Item -LiteralPath $feedPath).Length -gt 8192) { throw 'Compatibility feed configuration is too large.' }
    $feed = Get-Content -LiteralPath $feedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $names = @($feed.PSObject.Properties.Name)
    if ($names.Count -ne 3 -or @($names | Where-Object { $_ -cnotin @('ManifestUrl','Modulus','Exponent') }).Count) {
        throw 'Compatibility feed configuration must contain only ManifestUrl, Modulus and Exponent.'
    }
    $uri = [uri]$feed.ManifestUrl
    $modulus = [Convert]::FromBase64String($feed.Modulus)
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or -not $uri.AbsolutePath.EndsWith('/manifest.json') -or $feed.Exponent -cne 'AQAB' -or $modulus.Length -lt 256 -or $modulus.Length -gt 512 -or $modulus[0] -lt 128) {
        throw 'Compatibility feed configuration is invalid.'
    }
    $feedResource = Join-Path $outputDir 'compatibility-feed.public.json'
    $publicValues = [ordered]@{ ManifestUrl = $uri.AbsoluteUri; Modulus = [Convert]::ToBase64String($modulus); Exponent = 'AQAB' }
    [IO.File]::WriteAllText($feedResource, ($publicValues | ConvertTo-Json -Compress))
    $commonArgs += '/resource:' + $feedResource + ',compatibility-feed.json'
}
$commonArgs += '/resource:' + (Join-Path $projectRoot 'characters.json') + ',characters.json'
$iconPath = Join-Path $projectRoot 'assets\umapyon.ico'
$commonArgs += '/resource:' + $iconPath + ',umapyon.ico'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'assets\umapyon.png') + ',umapyon.png'
$commonArgs += '/win32icon:' + $iconPath
$commonArgs += '/resource:' + (Join-Path $projectRoot 'songs.json') + ',songs.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'concert-details.json') + ',concert-details.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'career-labels.json') + ',career-labels.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'race-catalog.json') + ',race-catalog.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'career-ranks.json') + ',career-ranks.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'card-dresses.json') + ',card-dresses.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'legacy-factors.json') + ',legacy-factors.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'race-courses.json') + ',race-courses.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'race-profiles.json') + ',race-profiles.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'music-assets.json') + ',music-assets.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'original-concert-covers.json') + ',original-concert-covers.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'character-assets.json') + ',character-assets.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'student-portraits.json') + ',student-portraits.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'racing-portraits.json') + ',racing-portraits.json'
$commonArgs += '/resource:' + (Join-Path $projectRoot 'outfit-catalog.json') + ',outfit-catalog.json'
if (-not $PublicRelease -or $PisaDev) { $commonArgs += '/resource:' + (Join-Path $projectRoot 'assets\special-week.png') + ',special-week.png' }
if (-not $TestsOnly) {
    & (Join-Path $dotnetDir 'dotnet.exe') $compiler.FullName @commonArgs /target:winexe /main:UmapyonPresence.Program ('/out:' + (Join-Path $outputDir 'UmaPresence.exe'))
    if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
}
& (Join-Path $dotnetDir 'dotnet.exe') $compiler.FullName @commonArgs /target:exe /main:UmapyonPresence.Tests ('/out:' + (Join-Path $outputDir 'UmapyonPresence.Tests.exe'))
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $outputDir 'UmaPresence.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $outputDir 'UmapyonPresence.Tests.exe.config') -Force
Write-Output "Built: $outputDir\UmaPresence.exe"
