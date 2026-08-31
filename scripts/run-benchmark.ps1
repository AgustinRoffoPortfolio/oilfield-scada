# Corre un escenario de benchmark de punta a punta y reporta las cuatro metricas.
# Levanta los cinco procesos, espera, consulta la base y baja todo.
#
# Uso:  .\scripts\run-benchmark.ps1 -Scenario 500 -Minutes 5
#       .\scripts\run-benchmark.ps1 -Scenario base -Minutes 5

param(
    [Parameter(Mandatory = $true)]
    [string] $Scenario,          # "base" o la cantidad de tags: 500 / 5000 / 15000

    [int] $Minutes = 5,

    # Sin valor por defecto a proposito: salen del .env, que no se versiona.
    # Se pueden pisar por parametro para correr contra otra base.
    [string] $DbPassword,
    [string] $DbUser,
    [string] $DbName
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

# --- Credenciales desde .env (mismo criterio que start-all.ps1).
if (-not (Test-Path .env)) { throw "Falta el archivo .env en la raiz del repo." }

$envVars = @{}
Get-Content .env | Where-Object { $_ -match '=' -and -not $_.StartsWith('#') } | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $envVars[$k.Trim()] = $v.Trim()
}

if (-not $DbUser)     { $DbUser     = $envVars['POSTGRES_USER'] }
if (-not $DbName)     { $DbName     = $envVars['POSTGRES_DB'] }
if (-not $DbPassword) { $DbPassword = $envVars['POSTGRES_PASSWORD'] }

if (-not $DbUser -or -not $DbName -or -not $DbPassword) {
    throw "El .env no tiene POSTGRES_USER / POSTGRES_DB / POSTGRES_PASSWORD."
}

# El escenario "base" usa el campo real; el resto, un archivo generado.
$addressSpaceFile = if ($Scenario -eq "base") {
    "config/addressspace.json"
} else {
    "config/bench-$Scenario.json"
}

if (-not (Test-Path (Join-Path $repoRoot $addressSpaceFile))) {
    throw "No existe $addressSpaceFile. Genera los escenarios con gen-bench-addressspace.ps1"
}

function Invoke-Sql([string] $query) {
    docker compose exec -T timescaledb psql -U $DbUser -d $DbName -t -A -c $query
}

Write-Host "`n=== Escenario: $Scenario ($addressSpaceFile), $Minutes min ===" -ForegroundColor Cyan

# --- Limpieza: cada corrida arranca de cero para que las cuentas sean del escenario.
Write-Host "Limpiando measurements y alarm_events..."
# tags va incluido. El sync de la ingesta consume un valor de la secuencia por cada
# tag que intenta insertar, exista o no, asi que las corridas del escenario grande
# queman 15.005 numeros cada una. Con tag_id SMALLINT (techo 32.767) la secuencia se
# agota en pocas corridas y la ingesta no llega a suscribirse.
# RESTART IDENTITY la devuelve a 1; CASCADE por las claves foraneas de measurements.
Invoke-Sql "TRUNCATE tags, measurements, alarm_events RESTART IDENTITY CASCADE;" | Out-Null

$logDir = Join-Path $repoRoot "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Get-ChildItem $logDir -Filter "ingestion-*.log" | Remove-Item -Force -ErrorAction SilentlyContinue

# --- Arranque de los cinco procesos.
# Cada uno en su propia ventana: si algo falla, el error queda visible.
$procs = @()

function Start-App([string] $project, [hashtable] $envVars) {
    $envPrefix = ($envVars.GetEnumerator() | ForEach-Object { "`$env:$($_.Key)='$($_.Value)'" }) -join "; "
    $command = "$envPrefix; dotnet run --project $project"
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "Set-Location '$repoRoot'; $command" -PassThru
}

$dbEnv = @{ "Database__Password" = $DbPassword }
$serverEnv = @{ "OpcUa__AddressSpaceFile" = $addressSpaceFile; "OpcUa__BenchmarkMode" = "true" }
$ingestEnv = @{ "Database__Password" = $DbPassword; "AddressSpaceFile" = $addressSpaceFile }

Write-Host "Levantando Simulator..."
$procs += Start-App "src\Simulator" @{}
Start-Sleep -Seconds 3

Write-Host "Levantando OpcUaServer..."
$procs += Start-App "src\OpcUaServer" $serverEnv
Start-Sleep -Seconds 8      # el arbol de 15.000 nodos tarda en construirse

Write-Host "Levantando Ingestion..."
$procs += Start-App "src\Ingestion" $ingestEnv
Start-Sleep -Seconds 10     # la sincronizacion del catalogo y la suscripcion

Write-Host "Levantando Alarms..."
$procs += Start-App "src\Alarms" $dbEnv
Start-Sleep -Seconds 2

Write-Host "Levantando WebApp..."
$procs += Start-App "src\WebApp" $dbEnv
Start-Sleep -Seconds 5

# --- Ventana de medicion.
# Se descartan los primeros 30 s: el arranque tiene el pico de la primera lectura
# de todos los items, que no representa el regimen estacionario.
Write-Host "`nEstabilizando (30 s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

$windowStart = (Get-Date).ToUniversalTime()
Write-Host "Midiendo $Minutes minutos..." -ForegroundColor Yellow

for ($i = $Minutes; $i -gt 0; $i--) {
    Write-Host "  quedan $i min..."
    Start-Sleep -Seconds 60
}

$windowEnd = (Get-Date).ToUniversalTime()
$windowSeconds = ($windowEnd - $windowStart).TotalSeconds

# --- Metricas desde la base.
Write-Host "`nConsultando la base..." -ForegroundColor Yellow

$startIso = $windowStart.ToString("yyyy-MM-dd HH:mm:ss")
$endIso = $windowEnd.ToString("yyyy-MM-dd HH:mm:ss")

$rowCount = [double](Invoke-Sql "SELECT count(*) FROM measurements WHERE ts >= '$startIso' AND ts < '$endIso';")
$rowsPerSecond = $rowCount / $windowSeconds

$totalRows = [double](Invoke-Sql "SELECT count(*) FROM measurements;")
$totalBytes = [double](Invoke-Sql "SELECT hypertable_size('measurements');")
$bytesPerRow = if ($totalRows -gt 0) { $totalBytes / $totalRows } else { 0 }
$gbPerDay = ($rowsPerSecond * 86400 * $bytesPerRow) / 1GB

# Tiempo real de la consulta que alimenta el dashboard.
# La consulta de produccion, copiada de ReadingRepository.LatestSql. Si esa cambia,
# esta tiene que cambiar con ella: medir otra cosa que la que corre el dashboard
# invalida la columna entera.
$explain = Invoke-Sql @"
EXPLAIN (ANALYZE, FORMAT JSON)
SELECT t.name, t.equipment, t.variable, t.unit,
       t.eu_min, t.eu_max, t.warn_low, t.warn_high, t.alarm_low, t.alarm_high,
       m.value, m.quality, m.ts
FROM tags t
LEFT JOIN LATERAL (
    SELECT value, quality, ts
    FROM measurements
    WHERE tag_id = t.tag_id
    ORDER BY ts DESC
    LIMIT 1
) m ON true
ORDER BY t.equipment, t.variable;
"@
$latestQueryMs = ($explain | ConvertFrom-Json)[0].'Execution Time'

# La alternativa descartada en la Fase 4, medida al lado para ver si la decision
# sigue en pie a esta escala. No va a la tabla, va al texto.
$explainAlt = Invoke-Sql @"
EXPLAIN (ANALYZE, FORMAT JSON)
SELECT DISTINCT ON (tag_id) tag_id, ts, value, quality
FROM measurements ORDER BY tag_id, ts DESC;
"@
$altQueryMs = ($explainAlt | ConvertFrom-Json)[0].'Execution Time'

# --- Latencia desde el log de la ingesta.
$logFile = Get-ChildItem $logDir -Filter "ingestion-*.log" | Select-Object -First 1
$avgLatency = 0; $p95Latency = 0; $maxLatency = 0

if ($logFile) {
    $matches = Select-String -Path $logFile.FullName -Pattern 'prom (\d+) p95 (\d+) max (\d+)' -AllMatches

    # Se descartan las primeras 15 lineas (los ~30 s de estabilizacion).
    $samples = $matches | Select-Object -Skip 15 | ForEach-Object {
        [pscustomobject]@{
            Avg = [double]$_.Matches[0].Groups[1].Value
            P95 = [double]$_.Matches[0].Groups[2].Value
            Max = [double]$_.Matches[0].Groups[3].Value
        }
    }

    if ($samples) {
        $avgLatency = ($samples | Measure-Object Avg -Average).Average
        $p95Latency = ($samples | Measure-Object P95 -Average).Average
        $maxLatency = ($samples | Measure-Object Max -Maximum).Maximum
    }
}

# --- Reporte.
Write-Host "`n=== RESULTADOS: escenario $Scenario ===" -ForegroundColor Green
Write-Host ("Ventana medida:          {0:N0} s" -f $windowSeconds)
Write-Host ("Filas escritas:          {0:N0}" -f $rowCount)
Write-Host ("Filas por segundo:       {0:N1}" -f $rowsPerSecond)
Write-Host ("Bytes por fila:          {0:N1}" -f $bytesPerRow)
Write-Host ("Proyeccion en disco:     {0:N2} GB/dia" -f $gbPerDay)
Write-Host ("Consulta ultimos valores:{0,8:N1} ms  (LATERAL, la de produccion)" -f $latestQueryMs)
Write-Host ("  alternativa DISTINCT ON:{0,8:N1} ms" -f $altQueryMs)
Write-Host ("Latencia prom:           {0:N0} ms" -f $avgLatency)
Write-Host ("Latencia p95:            {0:N0} ms" -f $p95Latency)
Write-Host ("Latencia max:            {0:N0} ms" -f $maxLatency)

# Fila lista para pegar en la tabla de docs/.
Write-Host "`nFila markdown:" -ForegroundColor Cyan
Write-Host ("| {0} | {1:N1} | {2:N0} | {3:N0} | {4:N1} | {5:N2} |" -f `
    $Scenario, $rowsPerSecond, $avgLatency, $p95Latency, $latestQueryMs, $gbPerDay)

# --- Bajada.
# Matar la ventana no alcanza: "dotnet run" lanza el binario como proceso hijo,
# que queda huerfano y sigue corriendo. Hay que bajar el arbol completo.
Write-Host "`nCerrando procesos..." -ForegroundColor Yellow

foreach ($p in $procs) {
    if (-not $p.HasExited) {
        # /T mata el arbol de procesos, /F sin pedir permiso.
        taskkill /PID $p.Id /T /F 2>&1 | Out-Null
    }
}

Start-Sleep -Seconds 2

# Red de seguridad: cualquier dotnet que haya sobrevivido al arbol.
$sobrevivientes = Get-Process dotnet -ErrorAction SilentlyContinue
if ($sobrevivientes) {
    Write-Host "  $($sobrevivientes.Count) procesos dotnet sobrevivientes, forzando..."
    $sobrevivientes | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host "Listo.`n" -ForegroundColor Green