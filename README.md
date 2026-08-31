# oilfield-scada

Sistema de monitoreo end-to-end de un yacimiento petrolero simulado: desde el modelo
físico de los pozos hasta un dashboard propio, pasando por Modbus TCP, OPC UA y un
historian de series temporales.

Es una rebanada vertical de lo que hace un SCADA real, construida para entender cada
capa en lugar de configurar un producto comercial.

## Demo

### La falla, de punta a punta

Degradación progresiva de una bomba electrosumergible: se inyecta la falla desde el
simulador, la vibración y la corriente suben juntas, el valor escala de aviso a alarma,
y el operador reconoce el evento. La pantalla arranca y termina en gris, que es como el
criterio ISA-101 dice que tiene que verse una planta sana.

https://github.com/user-attachments/assets/d64a95a4-0f6d-4766-ba61-06883231db88

### La cadena, capa por capa

La misma cadena vista por dentro: el mapa de registros Modbus del RTU, el árbol de
nodos del servidor OPC UA inspeccionado con UaExpert, la ingesta escribiendo por
lotes con su latencia medida, y el historial consultado en TimescaleDB.

https://github.com/user-attachments/assets/5639cd21-095c-47f7-8a76-add5d2c906b4

## Cadena de datos

```
Simulador (RTU)  →  Servidor OPC UA  →  Ingesta  →  TimescaleDB
(modelo físico,     (driver Modbus      (cliente    (historial)
 esclavo Modbus)     + address space)    OPC UA)         ↓
                                                  Motor de alarmas
       Modbus TCP           OPC UA                       ↓
       (registros)       (cifrado)           WebApp (ASP.NET Core)
                                                         ↓  SSE
                                              Dashboard (HTML/CSS/JS)
```

El simulador no publica OPC UA: expone una tabla de registros numerados por Modbus TCP,
igual que un RTU de campo. El servidor OPC UA no conoce el dominio —arma su árbol desde
un archivo de configuración y lo puebla con lo que le entrega el driver. Es el mismo
camino Modbus → OPC UA que se está migrando en la industria real.

## Estado

| Fase | Qué agrega | Estado |
|---|---|---|
| 1 | Modelo físico de pozos, separador y ducto | Completa |
| 2 | Servidor OPC UA | Completa |
| 3 | Ingesta y base de datos | Completa |
| 4 | Dashboard | Completa |
| 5 | Motor de alarmas | Completa |
| 6 | Address space configurable, driver Modbus TCP, seguridad OPC UA, benchmark de escala | Completa |
| 7 | Presentación: demo en video, documentación y decisiones de diseño | Completa |

El alcance de la Fase 6 se cerró en esos cuatro puntos. Planta compresora, medición
fiscal LACT, detección de anomalías y puente MQTT quedaron deliberadamente afuera:
son más equipos sobre una arquitectura que ya está demostrada de punta a punta, y no
agregan nada que el proyecto no muestre hoy. Las tres brechas conocidas contra un
sistema de producción —calidad del dato de campo, compresión y retención en la base,
y reintento de lotes fallidos en la ingesta— están documentadas en `docs/` y son
trabajo consciente pendiente, no descuido.

## Stack

**Backend:** C# / .NET 10, `OPCFoundation.NetStandard.Opc.Ua` (stack oficial de la OPC
Foundation), TimescaleDB, Npgsql + Dapper, Serilog, xUnit.

**Protocolo Modbus:** implementado a mano de los dos lados, sin paquete de terceros.

**Frontend:** HTML, CSS y JavaScript sin ninguna dependencia de terceros. Sin frameworks,
sin librerías de gráficos, sin build step. Los gráficos se dibujan a mano en Canvas 2D,
el mímico de proceso es SVG, y los datos en vivo llegan por Server-Sent Events, que es
API nativa del navegador.

**Infraestructura:** solo la base de datos corre en Docker; las aplicaciones .NET se
ejecutan con `dotnet run`.

## Qué se simula

Tres pozos con bomba electrosumergible (ESP), un separador y un tramo de ducto: 35 tags
en total. Los valores no son aleatorios, salen de un modelo físico donde las variables se
relacionan entre sí —si sube la frecuencia del variador sube el caudal y la corriente, si
la línea se obstruye sube la presión y cae el caudal— y donde lo que produce cada pozo es
exactamente lo que transporta el ducto.

Desde la consola del simulador se pueden inyectar fallas (teclas `1` a `4`): degradación
gradual de la bomba, obstrucción de línea, sensor congelado, deriva de sensor. La pérdida
de comunicación no se simula: se corta el proceso del simulador y el driver pierde el
socket de verdad.

## Documentación

- [`docs/arquitectura.md`](docs/arquitectura.md) — el modelo y cada decisión de diseño
- [`docs/glosario.md`](docs/glosario.md) — términos del dominio industrial
- [`docs/configuracion.md`](docs/configuracion.md) — el address space configurable
- [`docs/modbus.md`](docs/modbus.md) — mapa de registros y el driver
- [`docs/alarmas.md`](docs/alarmas.md) — motor de alarmas, histéresis y estados
- [`docs/seguridad.md`](docs/seguridad.md) — certificados, PKI y errores frecuentes

## Requisitos

- .NET SDK 10
- Docker Desktop
- [UaExpert](https://www.unified-automation.com/products/development-tools/uaexpert.html)
  (opcional, para inspeccionar el servidor OPC UA)

## Cómo levantarlo

Todos los comandos se corren **desde la raíz del repositorio**: varias rutas de
configuración son relativas a ella.

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

**3. Simulador / RTU** (primera terminal)

```powershell
dotnet run --project src\Simulator
```

Queda escuchando como esclavo Modbus TCP en el puerto 5502. Teclas `1` a `4` para
inyectar fallas.

**4. Servidor OPC UA** (segunda terminal)

```powershell
dotnet run --project src\OpcUaServer
```

Escucha en `opc.tcp://localhost:4840/OilfieldScada` y ofrece cuatro endpoints: uno sin
seguridad y tres cifrados con firma y encriptación. Arma su árbol de nodos desde
`config/addressspace.json` y lo puebla leyendo registros del RTU.

**5. Ingesta** (tercera terminal)

```powershell
$env:Database__Password = (Select-String -Path .\.env -Pattern '^POSTGRES_PASSWORD=(.*)$').Matches.Groups[1].Value
dotnet run --project src\Ingestion
```

Se suscribe a los 35 tags sobre canal cifrado y los persiste cada dos segundos. Si el
servidor OPC UA se cae, reintenta la conexión sola hasta recuperarla.

**6. Motor de alarmas** (cuarta terminal)

```powershell
$env:Database__Password = (Select-String -Path .\.env -Pattern '^POSTGRES_PASSWORD=(.*)$').Matches.Groups[1].Value
dotnet run --project src\Alarms
```

**7. WebApp** (quinta terminal)

```powershell
$env:Database__Password = (Select-String -Path .\.env -Pattern '^POSTGRES_PASSWORD=(.*)$').Matches.Groups[1].Value
dotnet run --project src\WebApp
```

Dashboard en `http://localhost:5080`.

### Primer arranque: certificados

La seguridad de OPC UA es de confianza mutua y manual, así que **la ingesta no conecta a
la primera**. Su certificado queda rechazado, hay que confiarlo, y después hay que
confiar el del servidor del lado del cliente:

```powershell
Move-Item pki\server\rejected\certs\*.der pki\server\trusted\certs\
Move-Item pki\ingestion\rejected\certs\*.der pki\ingestion\trusted\certs\
```

Al tercer intento conecta. El procedimiento completo y una tabla para interpretar los
errores están en [`docs/seguridad.md`](docs/seguridad.md).

**Verificar que hay datos:**

```powershell
docker compose exec timescaledb psql -U scada -d oilfield -c "SELECT t.name, m.ts, round(m.value::numeric,2) AS value FROM measurements m JOIN tags t ON t.tag_id=m.tag_id ORDER BY m.ts DESC LIMIT 10;"
```

## El dashboard

Sigue el criterio ISA-101 / High Performance HMI: fondo gris de baja saturación y valores
en escala de grises cuando todo está normal. El color está reservado para condiciones
anormales —ámbar para aviso, magenta para alarma, violeta para falta de dato— de modo que
una pantalla toda gris significa que la planta está bien. Cada valor lleva un indicador
analógico de rango al lado, porque la posición se lee más rápido que el dígito.

La pantalla principal es el mímico del proceso; el detalle por equipo se abre en un
faceplate flotante, encima, sin reemplazar la vista.

## Estructura

```
config/            Address space y mapa de registros Modbus (JSON)
src/Shared/        Modelo físico, compartido por todas las apps
src/Simulator/     El modelo corriendo como RTU esclavo Modbus TCP
src/OpcUaServer/   Servidor OPC UA + driver Modbus
src/Ingestion/     Cliente OPC UA que persiste en TimescaleDB
src/Alarms/        Motor de alarmas
src/WebApp/        API REST, SSE y dashboard
sql/               Esquema de la base
docs/              Arquitectura, glosario y documentación por componente
tests/             Tests del modelo físico y del motor de alarmas
pki/               Certificados OPC UA (no se versiona, se genera solo)
```

## Proyectos relacionados

- [`monitor-pozos`] — monitoreo de pozos
- [`monitor-gasoducto`] — monitoreo de gasoducto