# Genera address spaces sinteticos para el benchmark de escala.
# Toma el campo real y le agrega pozos clonados hasta llegar al total pedido.
# Los archivos generados NO van al repo: se regeneran con este script.

param(
    [int[]] $Targets = @(500, 5000, 15000)
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repoRoot "config\addressspace.json"

$base = Get-Content $sourcePath -Raw | ConvertFrom-Json

# Cuantos tags tiene cada tipo, para poder contar.
$tagsPerType = @{}
foreach ($type in $base.types) { $tagsPerType[$type.name] = $type.tags.Count }

$baseTagCount = 0
foreach ($device in $base.devices) { $baseTagCount += $tagsPerType[$device.type] }

$wellTags = $tagsPerType["WellType"]
Write-Host "Campo real: $($base.devices.Count) equipos, $baseTagCount tags"

foreach ($target in $Targets) {
    $extraWells = [Math]::Ceiling(($target - $baseTagCount) / $wellTags)
    if ($extraWells -lt 0) { $extraWells = 0 }

    $devices = [System.Collections.Generic.List[object]]::new()
    foreach ($device in $base.devices) { $devices.Add($device) }

    for ($i = 1; $i -le $extraWells; $i++) {
        $devices.Add([pscustomobject]@{
            name = "BENCH-{0:D4}" -f $i
            type = "WellType"
        })
    }

    $output = [pscustomobject]@{
        types   = $base.types
        devices = $devices
    }

    $total = $baseTagCount + ($extraWells * $wellTags)
    $outPath = Join-Path $repoRoot "config\bench-$target.json"
    $output | ConvertTo-Json -Depth 20 | Set-Content $outPath -Encoding UTF8

    Write-Host "  bench-$target.json -> $($devices.Count) equipos, $total tags"
}