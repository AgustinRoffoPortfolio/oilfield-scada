<#
    Arranca el sistema completo de oilfield-scada.
    Levanta TimescaleDB, compila la solucion y abre las cinco apps en ventanas
    separadas de PowerShell.

    Uso (desde cualquier lado):  .\scripts\start-all.ps1
#>

# 'Continue' a proposito: docker escribe warnings inofensivos en el canal de error
# y con 'Stop' PowerShell los trata como fatales. Cada paso critico se chequea a mano.
$ErrorActionPreference = 'Continue'

# La raiz del repo es el directorio padre de scripts/
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== oilfield-scada :: arranque ==" -ForegroundColor Cyan

# --- 1. Leer .env (sin mostrar valores) --------------------------------------
if (-not (Test-Path .env)) { throw "Falta el archivo .env en la raiz del repo." }

$env_vars = @{}
Get-Content .env | Where-Object { $_ -match '=' -and -not $_.StartsWith('#') } | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $env_vars[$k.Trim()] = $v.Trim()
}

$dbUser = $env_vars['POSTGRES_USER']
$dbPass = $env_vars['POSTGRES_PASSWORD']
$dbName = $env_vars['POSTGRES_DB']

if (-not $dbUser -or -not $dbPass -or -not $dbName) {
    throw "El .env no tiene POSTGRES_USER / POSTGRES_PASSWORD / POSTGRES_DB."
}

# Se setea una sola vez aca: los procesos hijos heredan el entorno del padre,
# asi que las ventanas la reciben sin tener que inyectarla en el comando.
$env:Database__Password = $dbPass

# --- 2. Verificar que el motor de Docker responda ----------------------------
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Docker no responde. Abri Docker Desktop, espera a que el icono quede estable y volve a correr el script."
}

# --- 3. Docker + base --------------------------------------------------------
Write-Host "-- Levantando TimescaleDB..." -ForegroundColor Yellow
docker compose up -d 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Fallo 'docker compose up'. Revisa: docker compose logs timescaledb" }

Write-Host "-- Esperando a que la base este lista..." -NoNewline
$ready = $false
foreach ($i in 1..30) {
    $health = docker inspect -f '{{.State.Health.Status}}' oilfield-timescaledb 2>&1
    if ($health -eq 'healthy') { $ready = $true; break }
    Start-Sleep -Seconds 2
    Write-Host "." -NoNewline
}
Write-Host ""
if (-not $ready) { throw "TimescaleDB no llego a estado healthy. Revisa: docker compose logs timescaledb" }

# --- 4. Esquema (solo si la tabla tags no existe) ----------------------------
$tagsTable = docker compose exec -T -e PGPASSWORD=$dbPass timescaledb `
    psql -U $dbUser -d $dbName -tAc "SELECT to_regclass('public.tags')" 2>$null

if ([string]::IsNullOrWhiteSpace($tagsTable)) {
    Write-Host "-- Base vacia: aplicando esquema..." -ForegroundColor Yellow
    foreach ($f in @('001_schema.sql', '03_tag_limits.sql', '04_alarms.sql')) {
        Write-Host "   $f"
        docker compose cp "sql\$f" "timescaledb:/tmp/$f" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "No se pudo copiar $f al contenedor." }

        docker compose exec -T -e PGPASSWORD=$dbPass timescaledb `
            psql -U $dbUser -d $dbName -v ON_ERROR_STOP=1 -q -f "/tmp/$f" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Fallo al aplicar $f. Corrarlo a mano para ver el error." }
    }
    Write-Host "   Esquema aplicado." -ForegroundColor Green
} else {
    Write-Host "-- Esquema ya presente, no se toca." -ForegroundColor Green
}

# --- 5. Compilar UNA sola vez ------------------------------------------------
# Las cinco apps referencian Shared. Si cada ventana compila por su cuenta, se
# pelean por escribir Shared.dll y la que pierde no arranca. Compilamos aca y
# despues cada app corre con --no-build.
Write-Host "-- Compilando la solucion..." -ForegroundColor Yellow
dotnet build OilfieldScada.slnx -v quiet --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Fallo la compilacion. Revisa los errores arriba; no se abrio ninguna ventana."
}
Write-Host "   Compilacion OK." -ForegroundColor Green

# --- 6. Aviso de PKI en el primer arranque -----------------------------------
$firstRun = -not (Test-Path 'pki')
if ($firstRun) {
    Write-Host ""
    Write-Host "!! PRIMER ARRANQUE: la PKI de OPC UA todavia no existe." -ForegroundColor Magenta
    Write-Host "   El servidor y la ingesta van a generar sus certificados y NO van a" -ForegroundColor Magenta
    Write-Host "   confiar entre si hasta que muevas los .der de rejected/ a trusted/" -ForegroundColor Magenta
    Write-Host "   de los dos lados. Procedimiento: docs\seguridad.md" -ForegroundColor Magenta
    Write-Host "   Despues de hacerlo, volve a correr este script." -ForegroundColor Magenta
    Write-Host ""
}

# --- 7. Abrir las apps -------------------------------------------------------
function Start-App {
    param(
        [string]$Title,
        [string]$Project
    )
    $inner = "`$Host.UI.RawUI.WindowTitle='$Title'; Set-Location '$root'; " +
             "dotnet run --no-build --project $Project"
    Start-Process powershell -ArgumentList '-NoExit', '-Command', $inner
    Write-Host "   -> $Title"
}

Write-Host "-- Abriendo aplicaciones..." -ForegroundColor Yellow
Start-App -Title 'Simulator'   -Project 'src\Simulator'
Start-Sleep -Seconds 2
Start-App -Title 'OpcUaServer' -Project 'src\OpcUaServer'
Start-Sleep -Seconds 5
Start-App -Title 'Ingestion'   -Project 'src\Ingestion'
Start-App -Title 'Alarms'      -Project 'src\Alarms'
Start-App -Title 'WebApp'      -Project 'src\WebApp'