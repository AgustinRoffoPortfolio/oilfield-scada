# Arquitectura

## Cadena de datos

Simulador → Servidor OPC UA → Ingesta → TimescaleDB
(modelo (expone tags) (cliente (historial)
físico) OPC UA) ↓
Motor de alarmas
↓
WebApp (ASP.NET Core)
↓ SSE
Dashboard (HTML/CSS/JS)


## Proyectos

| Proyecto | Tipo | Rol |
|---|---|---|
| `Shared` | Biblioteca | Modelos y configuración comunes |
| `Simulator` | Consola | Modelo físico de pozos, bombas, separador y ducto |
| `OpcUaServer` | Consola | Publica los tags del simulador vía OPC UA |
| `Ingestion` | Consola | Cliente OPC UA que persiste en TimescaleDB |
| `Alarms` | Biblioteca | Evaluación de umbrales con histéresis |
| `WebApp` | ASP.NET Core | API REST + SSE + dashboard estático |
| `Simulator.Tests` | xUnit | Tests del modelo físico y de las alarmas |

## Decisiones de diseño

- **Frontend sin dependencias.** Sin frameworks ni librerías: gráficos dibujados a mano en Canvas 2D, datos en vivo por Server-Sent Events (API nativa del navegador, con reconexión automática). No hay build step.
- **Procesos separados.** Cada etapa es un ejecutable independiente. Refleja cómo se despliega un SCADA real y permite frenar una parte sin voltear el resto.
- **Solo la base en Docker.** Las apps de .NET corren con `dotnet run`. Containerizar todo queda para una fase posterior.
- **Formato de solución `.slnx`.** Formato nuevo de .NET 10, en XML legible en vez de GUIDs.

## Fase 2 — Servidor OPC UA

### Qué hace esta fase

El simulador de la Fase 1 calculaba valores realistas pero los imprimía en una consola:
nadie más podía verlos. Esta fase los publica en la red con OPC UA, el protocolo
estándar de la industria, de modo que cualquier cliente —UaExpert, un SCADA comercial,
o el cliente propio que escribiremos en la Fase 3— pueda recorrer el yacimiento como un
árbol de carpetas, leer cada variable con su unidad de ingeniería y suscribirse a sus
cambios.

Endpoint publicado:

```
opc.tcp://localhost:4840/OilfieldScada
```

Stack: `OPCFoundation.NetStandard.Opc.Ua.Server` 1.5.378, la implementación oficial de la
OPC Foundation, certificada por un laboratorio de conformidad y la misma que se usa en
producción en la industria.

### Decisiones de diseño

**1. El servidor hospeda al simulador en el mismo proceso.**

`OpcUaServer` referencia a `Shared` y crea su propia instancia de `Oilfield`. No hay
comunicación entre procesos, ni colas, ni sockets internos entre el modelo físico y el
servidor.

Es lo que ocurre en campo: un PLC calcula y publica desde el mismo equipo, y el servidor
OPC UA es una capa de exposición sobre datos que ya tiene en memoria. Separarlos habría
significado inventar un protocolo intermedio —resolviendo dos veces el mismo problema que
OPC UA ya resuelve— para ganar una independencia que en este proyecto nadie necesita.

El proyecto `src/Simulator` sigue existiendo y corre por consola. Comparte el mismo
`Shared`, así que ambos ven exactamente el mismo modelo físico: sirve para demostrar el
simulador aislado, sin levantar la cadena completa.

**2. Un NodeManager propio, con namespace propio.**

El *address space* (el árbol de nodos) no es un objeto monolítico: se reparte entre
**NodeManagers**, cada uno dueño de una rama. `StandardServer` trae los suyos —el nodo
`Server`, los tipos estándar de la especificación— y nosotros registramos
`OilfieldNodeManager`, que construye y administra la rama `Oilfield`.

Cada NodeManager declara un **namespace**, en nuestro caso la URI
`http://oilfield-scada/`. El namespace garantiza que nuestros identificadores no choquen
con los del servidor ni con los de ningún otro fabricante: dos vendors pueden tener un
nodo llamado `THP` y no hay ambigüedad, porque el identificador completo incluye el
índice de namespace (`ns=2;s=POZO-A/THP`).

Es el mismo problema que en C se resuelve prefijando símbolos (`mimodulo_thp`), pero
formalizado por la especificación y resuelto en tiempo de ejecución.

**3. Tipos declarados, no carpetas sueltas.**

Había dos formas de armar el árbol. La simple: para cada equipo, crear una carpeta y
colgarle sus variables. La elegida: declarar **una vez** qué es un pozo —un `ObjectType`
llamado `WellType`, con sus diez tags marcados como `Mandatory`— y después crear tres
objetos que declaran ser de ese tipo. Lo mismo para `SeparatorType` y `PipelineType`.

En UaExpert el árbol de instancias se ve casi igual, pero hay dos diferencias que
importan:

- El tipo queda publicado en `Root → Types → ObjectTypes`. Un cliente puede preguntar
  "¿qué es esto?" y recibir "es un `WellType`, y todo `WellType` tiene THP, CHP,
  ESP_vib..." en lugar de "es una carpeta con cosas adentro".
- Los tags se definen en un solo lugar (`WellTagCatalog`, `SeparatorTagCatalog`,
  `PipelineTagCatalog`), que alimenta tanto la declaración del tipo como cada instancia.
  Agregar un tag nuevo es una línea, y aparece en el tipo y en todas las instancias a la
  vez.

Así funcionan las *companion specifications*: los modelos de información que la industria
publica por rubro (bombas, analizadores, máquinas herramienta) para que un equipo de
cualquier fabricante se describa de la misma manera.

Limitación conocida: el stack solo sabe instanciar un tipo automáticamente cuando el tipo
proviene de un archivo NodeSet o de código generado con el ModelCompiler. Los nuestros
están escritos a mano, así que las instancias se construyen recorriendo el mismo catálogo.
La ventaja del modelado se conserva —fuente única de verdad, tipo publicado y
consultable—; lo que no tenemos es la instanciación automática, que es una comodidad
interna y no algo que el cliente perciba.

**4. Endpoint sin seguridad, deliberadamente.**

Hoy el servidor expone un único endpoint con `SecurityPolicy None`, mensajes sin firmar
ni encriptar, y autenticación anónima. Es una decisión de secuencia, no un descuido: con
seguridad activada, cada cliente nuevo requiere aprobar manualmente su certificado en el
servidor y viceversa, y ningún error de conexión sería distinguible de un error del
modelo de datos. Habilitarla antes de tener el árbol funcionando habría convertido la
fase en una pelea con certificados.

La Fase 6 agrega el canal seguro: políticas `Basic256Sha256` con firma y encriptado,
validación real del certificado del cliente y una carpeta de rechazados que requiere
aprobación humana.

Lo que **sí** está resuelto desde ahora: el almacén de certificados (PKI) del servidor
vive en `%LocalAppData%\OPC Foundation\pki`, fuera del repositorio. No hay forma de
commitear una clave privada por accidente.

### Estructura del código

| Archivo | Responsabilidad |
|---|---|
| `Program.cs` | Lee la configuración, arma el logger, configura la aplicación OPC UA, crea el `Oilfield`, arranca el servidor y corre el timer de actualización. |
| `OpcUaOptions.cs` | Clase que espeja la sección `OpcUa` de `appsettings.json`. |
| `appsettings.json` | Endpoint, URI del namespace e intervalo de actualización. |
| `OilfieldServer.cs` | Hereda de `StandardServer`. Su único trabajo es registrar nuestro NodeManager. |
| `OilfieldNodeManager.cs` | Construye el árbol (tipos + instancias) y copia los valores del simulador a los nodos. |
| `WellTagCatalog.cs` | Define los tags de un pozo una sola vez: nombre, unidad, rango de escala y descripción. Contiene también el `record TagDefinition`. |
| `EquipmentTagCatalog.cs` | Lo mismo para el separador y el ducto. |

### Modelo de datos

Árbol resultante:

```
Root
└── Objects
    ├── Oilfield                    (FolderState)
    │   ├── POZO-A                  (BaseObjectState, TypeDefinition = WellType)
    │   │   ├── THP                 (AnalogItemState)
    │   │   │   ├── EngineeringUnits    → "bar"
    │   │   │   └── EURange             → 0 .. 60
    │   │   ├── CHP, T_head, Q_oil, Q_water, Q_gas,
    │   │   │   ESP_current, ESP_freq, ESP_vib
    │   │   └── Status              (MultiStateDiscreteState)
    │   │       └── EnumStrings         → [STOPPED, RUNNING, FAULT]
    │   ├── POZO-B
    │   ├── POZO-C
    │   ├── Separator               (TypeDefinition = SeparatorType)
    │   │   └── Sep_P, Sep_level
    │   └── Pipeline                (TypeDefinition = PipelineType)
    │       └── Pipe_P_in, Pipe_P_out, Pipe_Q
    └── Server                      (autodescripción, la trae el stack)

Types
└── ObjectTypes
    └── BaseObjectType
        ├── WellType                (10 tags, todos Mandatory)
        ├── SeparatorType           (2 tags)
        └── PipelineType            (3 tags)
```

El orden de las instancias sigue el recorrido del fluido: los pozos producen, el
separador recibe la suma de los tres, y el ducto transporta lo que sale del separador.
Ninguna etapa inventa datos; procesa lo que le entrega la anterior. Por eso una falla
inyectada en un pozo se propaga hasta la presión de salida del ducto sin que nadie
programe esa propagación.

Los tags analógicos usan `AnalogItemState`, el tipo estándar para una medición analógica.
Trae dos propiedades de fábrica:

- **EngineeringUnits** — la unidad (bar, °C, m³/d). Un cliente puede mostrar
  "33.7 bar" sin que nadie se lo haya configurado a mano.
- **EURange** — el rango de escala del **instrumento**, no el rango normal de operación.
  Un manómetro que solo llega a 40 bar no puede reportar una sobrepresión, así que las
  escalas se definen más amplias que el rango operativo. Los límites de operación se
  convertirán en umbrales de alarma en la Fase 5; son dos conceptos distintos.

| Tag | Unidad | Escala del instrumento | Rango normal de operación |
|---|---|---|---|
| `THP` | bar | 0 – 60 | 15 – 40 |
| `CHP` | bar | 0 – 40 | 8 – 25 |
| `T_head` | °C | 0 – 120 | 40 – 95 |
| `Q_oil` | m³/d | 0 – 150 | 5 – 120 |
| `Q_water` | m³/d | 0 – 250 | 0 – 200 |
| `Q_gas` | Nm³/d | 0 – 25000 | 500 – 20000 |
| `ESP_current` | A | 0 – 100 | 20 – 75 |
| `ESP_freq` | Hz | 0 – 70 | 35 – 60 |
| `ESP_vib` | mm/s | 0 – 12 | 0.5 – 7.0 |

| Tag | Equipo | Unidad | Escala del instrumento |
|---|---|---|---|
| `Sep_P` | Separador | bar | 0 – 20 |
| `Sep_level` | Separador | % | 0 – 100 |
| `Pipe_P_in` | Ducto | bar | 0 – 20 |
| `Pipe_P_out` | Ducto | bar | 0 – 20 |
| `Pipe_Q` | Ducto | m³/d | 0 – 800 |

El separador y el ducto no tienen tag de estado: no son equipos que arranquen y paren,
son recipientes y cañerías por donde pasa el fluido.

El estado del pozo usa `MultiStateDiscreteState`: guarda un entero y publica al lado la
lista de etiquetas en la propiedad `EnumStrings`. Es la diferencia con un `enum` de C,
donde los nombres desaparecen al compilar: acá el cliente lee el valor `1` y también las
etiquetas, y puede mostrar `RUNNING`.

Las etiquetas se derivan del enum `WellStatus` del simulador con
`Enum.GetNames<WellStatus>()`, no de una lista escrita a mano. La primera versión tenía
un array literal en otro orden que el enum, y un pozo produciendo se reportaba como
detenido. Derivarlas hace que la desincronización sea imposible.

### Ciclo de actualización

Un `Timer` en `Program.cs` dispara cada segundo:

1. `oilfield.Step(dt)` — avanza la física de todo el yacimiento.
2. `nodeManager.UpdateValues()` — copia cada `Sensor.Value` a su nodo y llama a
   `ClearChangeMasks`.

`ClearChangeMasks` es el aviso al stack de que el valor cambió. Sin esa llamada, un
cliente que lee explícitamente vería el dato nuevo, pero las **suscripciones** nunca se
dispararían.

Una suscripción es lo inverso a preguntar en un bucle: el cliente declara una sola vez
"avisame cuando estos tags se muevan" y el servidor le empuja los cambios. Es lo que
usa UaExpert al arrastrar un tag a la Data Access View, y lo que usará la ingesta de la
Fase 3.

Consecuencia observable: el `Status` muestra un timestamp más viejo que los analógicos.
Es correcto. Una suscripción notifica cuando cambia el **valor**, no cuando se refresca
el timestamp; como el estado no se movió, el cliente sigue mostrando la última
notificación recibida. Es la misma lógica del *deadband*: no gastar ancho de banda
reportando lo que no cambió.

El valor que se publica es `Sensor.Value` —lo que **reporta el instrumento**, con su
ruido y sus fallas— y no `Sensor.TrueValue`, que es el valor físico real. Esa distinción
viene del modelo de la Fase 1 y es lo que permite simular un sensor congelado o con
deriva: el SCADA ve lo que vería en la realidad, no la verdad del yacimiento.

### Configuración y logging

**Configuración.** El endpoint, la URI del namespace y el intervalo de actualización
viven en `appsettings.json` y se mapean a la clase `OpcUaOptions` con
`Microsoft.Extensions.Configuration`. Nada de eso está escrito en el código.

El intervalo se expresa en **milisegundos enteros** y no en segundos con decimales, a
propósito: el binder convierte texto a número según la cultura de la máquina, y en es-AR
el separador decimal es la coma. Un entero no puede interpretarse mal. Regla general para
este proyecto: en archivos de configuración, enteros y unidad explícita en el nombre de
la clave.

El paso de simulación se deriva del intervalo real (`interval.TotalSeconds`), así que
cambiar la frecuencia del ciclo no distorsiona la física.

**Logging.** Serilog, con salida estructurada a consola. Los mensajes usan plantillas con
propiedades nombradas (`"Servidor escuchando en {Endpoint}"`), de modo que el valor queda
guardado aparte del texto y se puede filtrar sin parsear cadenas.

La versión 1.5.378 del stack rehízo su modelo de observabilidad: reemplazó el logger
global por la interfaz `ITelemetryContext`, que envuelve una `ILoggerFactory` de .NET. El
servidor OPC UA no tiene logger propio, usa el que se le pase por constructor. Serilog se
conecta ahí:

```csharp
var telemetry = DefaultTelemetry.Create(builder => builder.AddSerilog(Log.Logger));
var application = new ApplicationInstance(telemetry) { ... };
```

Con eso, las trazas internas del servidor —sesiones creadas, suscripciones abiertas,
clientes que se caen— salen por el mismo canal que las nuestras. Es lo que permite
diagnosticar una pérdida de conexión sin adivinar.

El stack es muy verboso en nivel Information, así que sus categorías están elevadas a
Warning con `MinimumLevel.Override`. Un detalle no obvio: el servidor crea su logger con
el tipo **en tiempo de ejecución**, que es nuestra subclase, así que sus mensajes vienen
firmados como `OpcUaServer.OilfieldServer` y no como algo del namespace `Opc.Ua`. Hacen
falta las dos anulaciones.

El timer de actualización tiene su propio `try/catch`: una excepción no atrapada dentro
del callback de un `Timer` no la ve nadie y termina el proceso entero.

### Verificación

La fase se valida con **UaExpert**, el cliente de referencia de Unified Automation, que
es la misma herramienta con la que se verifica un servidor OPC UA en un proyecto real.

Conectando a `opc.tcp://localhost:4840/OilfieldScada` (endpoint `None - None`,
autenticación anónima) y arrastrando los equipos a la Data Access View, los valores se
actualizan cada segundo y las relaciones físicas se verifican a mano.

En un pozo, con el setpoint en 52 Hz y un corte de agua de 0.35:

| Tag | Valor observado | Relación esperada |
|---|---|---|
| `ESP_freq` | 51.98 Hz | setpoint 52, alcanzado con rampa de 0.5 Hz/s |
| `Q_oil + Q_water` | 173.0 m³/d | ley de afinidad: 200 × (52/60) = 173.3 |
| `Q_water / total` | 0.351 | corte de agua = 0.35 |
| `ESP_current` | 56.3 A | velocidad²: 75 × (52/60)² = 56.3 |
| `THP` | 33.7 bar | fricción por caudal²: 15 + 25 × 0.865² = 33.7 |
| `T_head` | 85.85 °C | aporte térmico: 25 + 70 × 0.865 = 85.6 |

Y aguas abajo, con los tres pozos produciendo:

| Tag | Valor observado | Relación esperada |
|---|---|---|
| `Pipe_P_in` | 10.32 bar | `Sep_P` (10.90) − pérdida en válvula (0.5) |
| `Pipe_P_out` | 3.98 bar | caída por fricción: 6 × (514.9/500)² = 6.36 bar |
| `Pipe_Q` | 514.9 m³/d | suma de `Q_oil` + `Q_water` de los tres pozos |
| `Sep_level` | 50.35 % | el control de nivel llegó a su setpoint de 50 |

Diez relaciones independientes que dan exacto. Es la diferencia entre un simulador con
un modelo físico detrás y una función de números aleatorios. El último renglón es el que
más vale: lo que sale del ducto es exactamente lo que produjeron los pozos, sin que nadie
programe esa correspondencia.

Al reiniciar el servidor, `Sep_P` y `Sep_level` no aparecen en su valor final: se acomodan
durante los primeros treinta segundos. Es la constante de tiempo del recipiente, porque un
separador tiene volumen y no salta.

### Pendientes conocidos

- **Un warning de API obsoleta**, en `AddSecurityConfiguration`: la versión nueva pide
  construir una colección de identificadores de certificado en lugar de un nombre de
  sujeto. Es tema de la Fase 6, cuando se configure la seguridad de verdad.
- **El logging solo va a consola.** Falta un sink a archivo con rotación diaria, que es
  lo mínimo para poder revisar qué pasó anoche. El nivel mínimo tampoco es configurable
  desde `appsettings.json` todavía.
- **El servidor no acepta escrituras.** Todos los tags son de solo lectura. Cambiar el
  setpoint de frecuencia de un pozo desde un cliente OPC UA sería el paso natural para
  demostrar el camino inverso, pero excede el alcance de un sistema de monitoreo.

## Fase 3 — Ingesta y base de datos

### Qué hace esta fase

La Fase 2 dejó los datos disponibles en la red, pero volátiles: quien no estuviera
conectado en el instante exacto de una medición, la perdía para siempre. Esta fase agrega
la memoria del sistema. Un cliente OPC UA propio se suscribe a los 35 tags del yacimiento
y los persiste en TimescaleDB, de modo que el dashboard de la Fase 4 pueda dibujar
tendencias y el motor de alarmas de la Fase 5 pueda razonar sobre lo que pasó, no solo
sobre el valor actual.

Es el punto donde el proyecto cruza de OT a IT: hasta acá todo era protocolo industrial;
de acá en adelante son bases de datos y HTTP.

### Decisiones de diseño

**1. Esquema largo, no una columna por tag.**

Cada lectura es una fila de `(instante, tag, valor, calidad)`, en lugar de una fila por
instante con 35 columnas. Es como guardan los historians reales —PI System, IP.21,
Canary— y la razón es que los datos **no llegan sincronizados**: con deadband, cada tag
reporta cuando cambia lo suficiente, así que una fila ancha obligaría a inventar valores
para las columnas que no se movieron.

El costo es que toda consulta necesita un `JOIN` con el catálogo de tags. La ventaja es
que agregar un pozo, un equipo o un tag no requiere tocar la estructura de la tabla: se
inserta una fila más en `tags` y listo.

**2. La calidad se guarda, no solo el valor.**

OPC UA no devuelve un número pelado: devuelve un `DataValue` con valor, timestamp y
`StatusCode`. Ese código dice si la lectura es confiable, dudosa o inválida —por ejemplo,
si el sensor se desconectó del PLC. Guardarlo permite que la Fase 5 no dispare alarmas
con datos basura.

El `StatusCode` completo son 32 bits con mucho detalle; acá se resume a un `SMALLINT`:
`0 = Good`, `1 = Uncertain`, `2 = Bad`. Es lo que se consulta en la práctica.

**3. El timestamp es el del servidor, no el de la escritura.**

Se guarda el `SourceTimestamp` del `DataValue` —el instante en que el valor se generó en
el origen— y no `DateTime.UtcNow` al momento de escribir. La diferencia importa: entre que
el dato se mide y se persiste hay latencia de red y hasta dos segundos de buffer. Un
historian tiene que reflejar cuándo pasó algo, no cuándo se enteró la base.

Todo se almacena en `TIMESTAMPTZ`, que Postgres normaliza a UTC. La conversión a hora
local es responsabilidad de quien muestra el dato.

**4. Deadband porcentual, configurable.**

Cada `MonitoredItem` analógico lleva un `DataChangeFilter` con deadband **porcentual**:
el umbral se expresa como porcentaje del rango de ingeniería del tag, así un solo número
sirve para presiones, caudales y vibración, cada uno escalado a su propia escala.

El valor está en `appsettings.json` (`DeadbandPercent`) y se calibró de forma empírica:

| Deadband | Filas por minuto | Efecto |
|---|---|---|
| 0.5 % | ~120 | Filtra casi todo el ruido; deja huecos de 20–30 s por tag |
| 0.2 % | ~350 | Silencia los tags estables, conserva densidad para graficar |
| 0 % | ~2100 | Sin filtro: 35 valores por segundo |

Se eligió 0.2 %. En un despliegue real el número se sube bastante más, porque el objetivo
es reducir tráfico y almacenamiento a lo largo de años; acá el objetivo es una demo de
minutos que tenga con qué dibujar una curva.

El `Status` de cada pozo se suscribe **sin filtro**: es un valor discreto, no tiene rango
de ingeniería, y cualquier cambio de estado interesa. El filtro usa
`DataChangeTrigger.StatusValue`, de modo que una caída de calidad también se reporta
aunque el número no se haya movido.

**5. El callback no escribe: encola.**

La notificación de la suscripción corre en el hilo del stack OPC UA y tiene que devolver
rápido. Por eso solo encola en una `ConcurrentQueue` en memoria; un loop aparte, con
`PeriodicTimer`, vacía la cola cada dos segundos y la escribe en un solo lote.

Desacoplar recepción de escritura es lo que hace cualquier ingesta seria: si la base se
pone lenta o se cae un momento, la lectura de OPC UA no se frena y el buffer absorbe la
diferencia. El volcado está envuelto en `try/catch` y un fallo pierde ese lote pero no
mata el proceso.

**6. Escritura con COPY binario.**

El lote se escribe con `COPY ... FROM STDIN (FORMAT BINARY)`, el mecanismo de carga
masiva de Postgres, en lugar de un `INSERT` por fila. Los valores viajan tipados
(`NpgsqlDbType.Double`), nunca convertidos a texto, lo que además elimina de raíz el
problema de la coma decimal de la cultura es-AR.

El índice `(tag_id, ts)` es **único**, así que un lote con un par repetido abortaría
entero. Antes de escribir se deduplica en memoria conservando la última lectura de cada
par.

**7. El catálogo de tags se sincroniza desde el modelo.**

La tabla `tags` no se puebla con `INSERT` escritos a mano. Al arrancar, la ingesta recorre
el `Oilfield` —de donde salen los nombres de los pozos— y los catálogos de tags, arma los
35 nombres calificados y los inserta con `ON CONFLICT DO NOTHING`. Es idempotente: correr
la app cien veces deja la tabla igual.

Para que esto fuera posible, `WellTagCatalog` y `EquipmentTagCatalog` se movieron de
`OpcUaServer` a `Shared`. Ahora el servidor y la ingesta leen la misma definición: los
nombres de la base no pueden desincronizarse de los NodeIds que publica OPC UA, porque
salen del mismo lugar.

**8. Reconexión automática.**

La sesión OPC UA mantiene un *keep-alive*: un latido periódico contra el servidor. Si
falla, el evento `KeepAlive` llega con estado malo y se dispara un
`SessionReconnectHandler`, que reintenta cada 5 segundos hasta recuperar la comunicación
y **reactiva la suscripción existente** en vez de crear una nueva.

Un flag interno distingue una caída real de un cierre ordenado de la aplicación; sin él,
apagar la ingesta a propósito dispararía un intento de reconexión.

Durante la caída no se escribe nada, y eso es deliberado: el historial queda con un hueco
que refleja que en ese lapso nadie midió. Interpolar habría sido inventar datos.

### Estructura del código

| Archivo | Responsabilidad |
|---|---|
| `Program.cs` | Arma la configuración y el logger, sincroniza el catálogo, conecta el cliente OPC UA, y corre el loop de volcado. |
| `DatabaseOptions.cs` | Espeja la sección `Database` de `appsettings.json` y construye la cadena de conexión. |
| `OpcUaOptions.cs` | Espeja la sección `OpcUa`: endpoint, intervalo de publicación, deadband, keep-alive y período de reintento. |
| `OpcUaClient.cs` | Sesión OPC UA, suscripción con deadband y reconexión automática. |
| `TagRepository.cs` | Sincroniza el catálogo de tags y devuelve el mapa `nombre → tag_id`. |
| `MeasurementRepository.cs` | Escritura masiva del lote con `COPY` binario. |
| `Measurement.cs` | El registro de una lectura lista para persistir. |
| `sql/001_schema.sql` | Creación de las tablas y de la hypertable. |
| `docker-compose.yml` | TimescaleDB, en la raíz del repositorio. |

### Esquema de la base

```sql
tags         (tag_id, name, equipment, variable, unit, eu_min, eu_max)
measurements (ts, tag_id, value, quality)   -- hypertable, chunk de 1 día
```

`tags` es un catálogo chico y estable: 35 filas, una por variable. `name` es el nombre
calificado (`POZO-A/THP`), idéntico al NodeId que publica el servidor OPC UA, y tiene
restricción de unicidad.

`measurements` es la serie temporal. Se convierte en **hypertable** con
`create_hypertable(...)`: TimescaleDB la parte por detrás en trozos por rango de fechas
—*chunks*—, de modo que una consulta acotada en el tiempo toca solo los trozos relevantes
en lugar de recorrer la tabla entera. Para el código SQL sigue siendo una tabla común.

El índice `(tag_id, ts DESC)` cubre la consulta típica del dashboard —la serie de un tag
en una ventana de tiempo, del más nuevo al más viejo— y de paso impide duplicados.

### Credenciales

Las de la base viven en un `.env` en la raíz, que Docker Compose lee al levantar el
contenedor y que **no se commitea**. Se versiona un `.env.example` con las claves vacías,
para que quien clone el repositorio sepa qué tiene que definir.

La ingesta no lee ese archivo: toma la contraseña de la variable de entorno
`Database__Password`. La configuración de .NET se arma por capas —primero
`appsettings.json`, después el entorno, que pisa lo anterior— y el doble guión bajo
representa el anidamiento. Así el resto de los parámetros de conexión quedan versionados y
visibles, y el único dato sensible nunca toca el repositorio.

### Verificación

Con TimescaleDB levantado, el servidor OPC UA corriendo y la ingesta en marcha, una
corrida de aproximadamente un minuto con deadband 0.2 % escribió 351 filas, y el `count`
de la tabla coincidió exactamente con el total reportado por la aplicación.

Consulta de control, que es también la que usará el dashboard:

```sql
SELECT t.name, m.ts, m.value, m.quality
FROM measurements m
JOIN tags t ON t.tag_id = m.tag_id
ORDER BY m.ts DESC
LIMIT 8;
```

**Prueba de reconexión.** Con la cadena corriendo se apagó el servidor OPC UA. La ingesta
reportó `BadConnectionClosed`, volcó lo que tenía en el buffer y quedó reintentando sin
morirse. Al levantar el servidor de nuevo, cuarenta segundos después, recuperó la sesión
con los 35 monitored items intactos y los volcados retomaron solos. El historial quedó con
un hueco de esos cuarenta segundos, que es el resultado correcto.

### Un error que costó y conviene no repetir

Durante buena parte de la fase, `appsettings.json` **no se estaba leyendo**. La aplicación
corría íntegramente con los valores por defecto de las clases de opciones, y nadie lo
notaba porque esos defaults coincidían con el contenido del archivo. El primer valor que
difirió —el deadband— fue el que delató el problema.

La causa: `Host.CreateApplicationBuilder` busca la configuración en el directorio de
trabajo, y la app se ejecuta con `dotnet run --project src\Ingestion` desde la raíz del
repositorio. El archivo se copia junto al ejecutable, no a la raíz, así que nunca lo
encontraba. Como la configuración es opcional por diseño, no hubo error ni advertencia.

Se corrige anclando el *content root* al directorio del ejecutable:

```csharp
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
```

`OpcUaServer` no tenía el problema: ya usaba `SetBasePath(AppContext.BaseDirectory)` desde
la Fase 2.

La lección de fondo es sobre el diagnóstico. Que un valor por defecto coincida con el
configurado hace invisible una falla de configuración; conviene verificar que la
configuración se lee leyendo una clave cruda (`Configuration["Seccion:Clave"]`) y no
inferirlo del comportamiento.

### Pendientes conocidos

- **Compresión sin configurar.** El argumento a favor del esquema largo se apoya en la
  compresión columnar de TimescaleDB, que agrupa por `tag_id` y reduce el tamaño de forma
  drástica. Todavía no está habilitada: con datos de minutos no cambia nada, pero es lo
  primero que habría que activar para un despliegue real.
- **Sin política de retención.** La tabla crece sin límite. TimescaleDB permite descartar
  chunks viejos automáticamente.
- **La ingesta no reintenta lotes fallidos.** Si el volcado falla, ese lote se pierde. Un
  buffer en disco o una cola persistente sería lo siguiente.
- **Warnings de API obsoleta** en el cliente OPC UA, de la misma familia que los del
  servidor: la versión 1.5.378 del stack migra a firmas que reciben un
  `ITelemetryContext`. Se resuelven junto con la seguridad, en la Fase 6.

## Consulta de ultimos valores (Fase 4)

El dashboard necesita el ultimo valor de los 35 tags en cada carga. Se probaron
dos formas contra 6.048.823 filas sinteticas repartidas en 6 chunks:

| Consulta | Tiempo | Bloques leidos |
|---|---|---|
| `LATERAL` + join a `tags` | 0,5 ms | 102 |
| `DISTINCT ON` sin join | 11,7 ms | 935 |
| `DISTINCT ON` + join a `tags` | 2.491 ms | 1.598.079 |

Se eligio **LATERAL**. Recorre los 35 tags del catalogo y por cada uno pide su
ultima medicion; el `ChunkAppend` de TimescaleDB va del chunk mas nuevo al mas
viejo y encuentra el dato en el primero, dejando los otros cinco en
`never executed`. El costo no crece con el historico acumulado.

`DISTINCT ON` puede usar el nodo `SkipScan` de TimescaleDB, pero es fragil: al
sumar el join con `tags` el planificador lo descarta sin aviso y pasa a leer las
6 millones de filas para devolver 35. Ademas el SkipScan se ejecuta una vez por
chunk, mientras que LATERAL frena en el primero.

Limitacion conocida: LATERAL es rapido porque todos los tags reportan seguido.
Un tag sin datos recientes obligaria a bajar chunk por chunk hasta encontrarlo.

Reproducible con `sql/dev_benchmark_seed.sql` y `sql/dev_benchmark_explain.sql`.
Los datos sinteticos se borran con
`SELECT drop_chunks('measurements', older_than => INTERVAL '10 days');`.

## Criterio visual: ISA-101 (Fase 4)

El dashboard sigue ISA-101 / High Performance HMI en vez de un tema oscuro
convencional. Fondo gris de baja saturacion, valores en escala de grises, y el color
reservado para condiciones anormales: una pantalla sin color significa planta normal.
Cada valor lleva una barra que muestra su posicion dentro del rango de ingenieria
(`eu_min`/`eu_max` del catalogo), porque la posicion se lee mas rapido que el digito.

La vejez del dato se evalua **sobre el conjunto de tags, no tag por tag**. Con deadband
activo un tag que no cambia deja de reportar y su timestamp envejece sin que el dato
sea invalido; lo que indica corte de la cadena es que *ninguno* de los 35 reporte.

## Fase 4 — Dashboard

`src/WebApp` es ASP.NET Core con tres endpoints y archivos estaticos en `wwwroot`.
La WebApp **lee de TimescaleDB**, no abre su propia sesion OPC UA: la ingesta es la
unica puerta de entrada de datos, y a cambio de hasta 2 s de retraso la capa de datos
queda desacoplada del frontend.

- `GET /api/tags/latest` — ultimo valor de los 35 tags (consulta LATERAL, ver seccion
  anterior).
- `GET /api/history?tag=&minutes=` — historial agregado con `time_bucket`, apuntando a
  ~600 puntos sin importar la ventana. Devuelve avg, min y max por intervalo: promediar
  solo esconderia los picos, que es justo lo que no puede perder un historian.
- `GET /api/stream` — Server-Sent Events. Un unico `BackgroundService` consulta la base
  una vez por segundo y reparte el snapshot a todos los conectados por `Channel<T>`,
  en vez de que cada cliente consulte por su cuenta. Con N pestañas abiertas la carga
  sobre la base sigue siendo una consulta por segundo.

Frontend sin dependencias: HTML, CSS y JS nativo, modulos ES sin build step.
`chart.js` es un motor de graficos propio en Canvas 2D — mapeo dato/pixel, ticks
calculados redondeando al 1, 2 o 5 mas cercano, correccion de `devicePixelRatio` para
pantallas HiDPI, y corte de la linea cuando hay un hueco en los datos en vez de unir
dos puntos con una recta que nunca existio.

El autoescalado vertical tiene un piso del 10 % del rango de ingenieria. Sin eso, un
tag estable con deadband se dibuja como un terremoto: el grafico termina mostrando el
deadband en lugar del proceso.

Ningún componente del frontend tiene listas de equipos escritas a mano: los grupos se
derivan del campo `equipment` que viene en los datos, y un equipo nuevo aparece solo.

## Fase 4, paso 7 — Mímico, faceplates y condiciones anormales

La pantalla principal es un **esquema de proceso** en SVG: los tres pozos con su
cabezal y su ESP, las líneas convergiendo al colector, el separador y el ducto con sus
transmisores. Las tarjetas por equipo que existían antes se eliminaron; su rol lo
cumple ahora el faceplate.

### Tres niveles de navegación

| Nivel | Qué muestra | Responde |
|---|---|---|
| Mímico | El yacimiento completo | ¿Dónde está el problema? |
| Faceplate | Un equipo, todas sus variables | ¿Qué le pasa a este equipo? |
| Tendencia | Un tag, hasta 24 h | ¿Desde cuándo? |

Es la jerarquía de un SCADA real, y el recorrido es un clic por nivel: se toca un
equipo en el esquema y se abre su faceplate; se toca una variable adentro y va al
gráfico grande.

### Por qué faceplate flotante y no filtrar la pantalla

La primera versión filtraba las tarjetas al equipo elegido. Se descartó por dos
razones. La primera es que **el mímico no se tiene que mover nunca**: el operador
memoriza dónde está cada equipo en la pantalla y lo ubica por reflejo; cualquier
reacomodo destruye esa memoria espacial. La segunda es que el filtrado solo permite
mirar un equipo por vez, mientras que dos faceplates abiertos dejan comparar POZO-A
con POZO-C lado a lado.

Cada faceplate incluye su propia mini-tendencia de 30 minutos, con el mismo motor
Canvas del gráfico grande. No es adorno: la falla característica del ESP es la
degradación gradual, y **es invisible en el valor instantáneo**. Una vibración de
2,4 mm/s no dice nada; 2,4 subiendo desde 1,9 en veinte minutos dice que la bomba se
está yendo. Esa falla solo existe en la tendencia.

### Umbrales: configuración de planta, no de la pantalla

Las condiciones anormales se evalúan contra cuatro columnas del catálogo de tags:
`warn_low`, `warn_high`, `alarm_low`, `alarm_high`. Viven en la base porque los
límites de alarma son configuración de planta: cambiarlos no debería requerir tocar
el frontend, y el motor de alarmas de la Fase 5 lee exactamente los mismos valores.

Un umbral en `NULL` significa "sin límite de ese lado". La vibración no tiene mínimo:
cuanto menos, mejor.

Cuatro columnas y no dos porque *prestá atención* y *actuá ahora* son cosas distintas.
La vibración a 5,5 mm/s se está yendo; a 7,0 hay que parar la bomba.

**Los límites no se ponen en el borde del rango nominal.** El rango de la tabla de
dominio describe lo que el proceso hace, no dónde hay que avisar. POZO-C opera
establemente en 146 m³/d contra un nominal de 120, así que su umbral quedó en 160: un
aviso que suena durante la operación normal entrena al operador a ignorarlo, que es el
modo de falla más común de los SCADA reales.

### Uso del color

Gris para todo lo normal. Ámbar para `warn`, magenta para `alarm`, violeta para dato
inválido o comunicación caída. El magenta se eligió sobre el rojo porque se distingue
bajo daltonismo, que es la recomendación de ISA-101.

Un equipo se pinta con el peor estado de sus variables, y `Status = FAULT` lo manda
directo a alarma. Si se corta la cadena de datos, los cinco equipos pasan a violeta
juntos: no saber qué está pasando es peor que saber que algo está mal.

### Estructura del frontend

| Archivo | Responsabilidad |
|---|---|
| `index.html` | Geometría del mímico. Cada equipo lleva `data-equipment`; cada hueco de valor, `data-variable`. |
| `mimic.js` | Escribe valores y estados sobre el esquema. No sabe nada de geometría: encuentra los nodos por atributo. |
| `faceplate.js` | Ventanas flotantes: arrastre, apilado, cierre con `Escape`, mini-tendencia propia. |
| `state.js` | Clasifica una lectura contra sus umbrales. Única definición de "esto está mal". |
| `format.js` | Decimales según rango y texto de estado, compartidos por todos. |
| `chart.js` | Motor de gráficos en Canvas 2D, reusado por el gráfico grande y por cada faceplate. |
| `app.js` | Cablea las piezas: conexión SSE, selección del gráfico grande, apertura de faceplates. |

El HTML define **dónde** va cada cosa y el JS **qué** dice. El mímico es un dibujo de
ingeniería: la posición de cada equipo es una decisión de proceso, no algo que se
derive de los datos.