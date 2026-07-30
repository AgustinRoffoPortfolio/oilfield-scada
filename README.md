# oilfield-scada

Sistema de monitoreo end-to-end de un yacimiento petrolero simulado: desde el modelo
físico de los pozos hasta un dashboard propio, pasando por OPC UA y un historian de
series temporales.

Es una rebanada vertical de lo que hace un SCADA real, construida para entender cada
capa en lugar de configurar un producto comercial.

## Cadena de datos

```
Simulador  →  Servidor OPC UA  →  Ingesta  →  TimescaleDB
(modelo       (expone tags)       (cliente    (historial)
 físico)                           OPC UA)         ↓
                                            Motor de alarmas
                                                   ↓
                                        WebApp (ASP.NET Core)
                                                   ↓  SSE
                                        Dashboard (HTML/CSS/JS)
```

## Estado

| Fase | Qué agrega | Estado |
|---|---|---|
| 1 | Modelo físico de pozos, separador y ducto | Completa |
| 2 | Servidor OPC UA | Completa |
| 3 | Ingesta y base de datos | Completa |
| 4 | Dashboard | Pendiente |
| 5 | Motor de alarmas | Pendiente |
| 6 | Seguridad OPC UA y diferenciales | Pendiente |

## Stack

**Backend:** C# / .NET 10, `OPCFoundation.NetStandard.Opc.Ua` (stack oficial de la OPC
Foundation), TimescaleDB, Npgsql + Dapper, Serilog.

**Frontend:** HTML, CSS y JavaScript sin ninguna dependencia de terceros. Sin frameworks,
sin librerías de gráficos, sin build step. Los gráficos se dibujan a mano en Canvas 2D y
los datos en vivo llegan por Server-Sent Events, que es API nativa del navegador.

**Infraestructura:** solo la base de datos corre en Docker; las aplicaciones .NET se
ejecutan con `dotnet run`.

## Qué se simula

Tres pozos con bomba electrosumergible (ESP), un separador y un tramo de ducto: 35 tags
en total. Los valores no son aleatorios, salen de un modelo físico donde las variables se
relacionan entre sí —si sube la frecuencia del variador sube el caudal y la corriente, si
la línea se obstruye sube la presión y cae el caudal— y donde lo que produce cada pozo es
exactamente lo que transporta el ducto.

El detalle del modelo y de cada decisión de diseño está en [`docs/arquitectura.md`](docs/arquitectura.md).
Los términos del dominio industrial están explicados en [`docs/glosario.md`](docs/glosario.md).

## Requisitos

- .NET SDK 10
- Docker Desktop
- [UaExpert](https://www.unified-automation.com/products/development-tools/uaexpert.html)
  (opcional, para inspeccionar el servidor OPC UA)

## Cómo levantarlo

**1. Credenciales de la base**

Copiar `.env.example` a `.env` y completar la contraseña:

```powershell
Copy-Item .env.example .env
```

Este archivo no se versiona.

**2. Base de datos**

```powershell
docker compose up -d
Get-Content .\sql\001_schema.sql | docker compose exec -T timescaledb psql -U scada -d oilfield
```

**3. Servidor OPC UA** (primera terminal)

```powershell
dotnet run --project src\OpcUaServer
```

Queda escuchando en `opc.tcp://localhost:4840/OilfieldScada`, con política de seguridad
`None` y autenticación anónima. Se puede inspeccionar con UaExpert.

**4. Ingesta** (segunda terminal)

```powershell
$env:Database__Password = (Select-String -Path .\.env -Pattern '^POSTGRES_PASSWORD=(.*)$').Matches.Groups[1].Value
dotnet run --project src\Ingestion
```

Se suscribe a los 35 tags y los persiste cada dos segundos. Si el servidor OPC UA se cae,
reintenta la conexión sola hasta recuperarla.

**Verificar que hay datos:**

```powershell
docker compose exec timescaledb psql -U scada -d oilfield -c "SELECT t.name, m.ts, round(m.value::numeric,2) AS value FROM measurements m JOIN tags t ON t.tag_id=m.tag_id ORDER BY m.ts DESC LIMIT 10;"
```

## Estructura

```
src/Shared/        Modelo físico y catálogo de tags, compartidos por todas las apps
src/Simulator/     El modelo corriendo por consola, sin OPC UA
src/OpcUaServer/   Servidor OPC UA que publica los tags
src/Ingestion/     Cliente OPC UA que persiste en TimescaleDB
src/Alarms/        Motor de alarmas (Fase 5)
src/WebApp/        API REST, SSE y dashboard (Fase 4)
sql/               Esquema de la base
docs/              Arquitectura y glosario
tests/             Tests del modelo físico
```

## Proyectos relacionados

- [`monitor-pozos`] — monitoreo de pozos
- [`monitor-gasoducto`] — monitoreo de gasoducto