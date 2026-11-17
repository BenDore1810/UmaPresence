param([Parameter(Mandatory=$true)][string]$Path, [switch]$PisaDev)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.TypeDefinitions) {
            $type = $metadata.GetTypeDefinition($handle)
            $name = $metadata.GetString($type.Name)
            if ($name -like '*Tests' -or $name -in @('PortraitBaker','CoverExport')) { throw "Development type in production EXE: $name" }
            if ($name -eq 'IntegrationProfiles') {
                foreach ($field in $type.GetFields()) {
                    if ($metadata.GetString($metadata.GetFieldDefinition($field).Name) -eq 'Fault') {
                        throw 'Installer fault hook in production EXE'
                    }
                }
            }
        }
        $resources = foreach ($handle in $metadata.ManifestResources) {
            $metadata.GetString($metadata.GetManifestResource($handle).Name)
        }
        if ($PisaDev.IsPresent -ne ('special-week.png' -in $resources)) { throw 'Unexpected artwork resource for this variant' }
        Write-Output "Production EXE metadata passed: $Path"
    } finally { $pe.Dispose() }
} finally { $stream.Dispose() }
