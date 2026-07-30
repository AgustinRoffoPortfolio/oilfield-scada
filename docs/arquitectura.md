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

**3. `WellType` como tipo, no tres carpetas sueltas.**

Había dos formas de armar el árbol. La simple: para cada pozo, crear una carpeta y
colgarle sus diez variables. La elegida: declarar **una vez** qué es un pozo —un
`ObjectType` llamado `WellType`, con sus diez tags marcados como `Mandatory`— y después
crear tres objetos que declaran ser de ese tipo.

En UaExpert el árbol de instancias se ve casi igual, pero hay dos diferencias que
importan:

- El tipo queda publicado en `Root → Types → ObjectTypes`. Un cliente puede preguntar
  "¿qué es esto?" y recibir "es un `WellType`, y todo `WellType` tiene THP, CHP,
  ESP_vib..." en lugar de "es una carpeta con cosas adentro".
- Los tags se definen en un solo lugar (`WellTagCatalog`), que alimenta tanto la
  declaración del tipo como cada instancia. Agregar un tag nuevo es una línea, y aparece
  en el tipo y en los tres pozos a la vez.

Así funcionan las *companion specifications*: los modelos de información que la industria
publica por rubro (bombas, analizadores, máquinas herramienta) para que un equipo de
cualquier fabricante se describa de la misma manera.

Limitación conocida: el stack solo sabe instanciar un tipo automáticamente cuando el tipo
proviene de un archivo NodeSet o de código generado con el ModelCompiler. El nuestro está
escrito a mano, así que las instancias se construyen recorriendo el mismo catálogo. La
ventaja del modelado se conserva —fuente única de verdad, tipo publicado y consultable—;
lo que no tenemos es la instanciación automática, que es una comodidad interna y no algo
que el cliente perciba.

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
| `Program.cs` | Configura la aplicación OPC UA, crea el `Oilfield`, arranca el servidor y corre el timer de actualización. |
| `OilfieldServer.cs` | Hereda de `StandardServer`. Su único trabajo es registrar nuestro NodeManager. |
| `OilfieldNodeManager.cs` | Construye el árbol (tipo + instancias) y copia los valores del simulador a los nodos. |
| `WellTagCatalog.cs` | Define los tags de un pozo una sola vez: nombre, unidad, rango de escala y descripción. |

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
    │   └── POZO-C
    └── Server                      (autodescripción, la trae el stack)

Types
└── ObjectTypes
    └── BaseObjectType
        └── WellType                (los 10 tags, todos Mandatory)
```

Los nueve tags analógicos usan `AnalogItemState`, el tipo estándar para una medición
analógica. Trae dos propiedades de fábrica:

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

1. `oilfield.Step(1.0)` — avanza un segundo de física en todo el yacimiento.
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

### Verificación

La fase se valida con **UaExpert**, el cliente de referencia de Unified Automation, que
es la misma herramienta con la que se verifica un servidor OPC UA en un proyecto real.

Conectando a `opc.tcp://localhost:4840/OilfieldScada` (endpoint `None - None`,
autenticación anónima) y arrastrando POZO-A a la Data Access View, los valores se
actualizan cada segundo y las relaciones físicas se verifican a mano. Con el setpoint en
52 Hz y un corte de agua de 0.35:

| Tag | Valor observado | Relación esperada |
|---|---|---|
| `ESP_freq` | 51.98 Hz | setpoint 52, alcanzado con rampa de 0.5 Hz/s |
| `Q_oil + Q_water` | 173.0 m³/d | ley de afinidad: 200 × (52/60) = 173.3 |
| `Q_water / total` | 0.351 | corte de agua = 0.35 |
| `ESP_current` | 56.3 A | velocidad²: 75 × (52/60)² = 56.3 |
| `THP` | 33.7 bar | fricción por caudal²: 15 + 25 × 0.865² = 33.7 |
| `T_head` | 85.85 °C | aporte térmico: 25 + 70 × 0.865 = 85.6 |

Seis relaciones independientes que dan exacto. Es la diferencia entre un simulador con
un modelo físico detrás y una función de números aleatorios.

### Pendientes conocidos

Anotados deliberadamente, no olvidados:

- **Separador y ducto todavía no se exponen.** El modelo de la Fase 1 los simula y el
  acoplamiento funciona, pero el árbol OPC UA solo publica los tres pozos. Falta un
  `SeparatorType` y un `PipelineType` con el mismo criterio.
- **El puerto está hardcodeado** en `Program.cs`. Debe moverse a `appsettings.json` junto
  con el intervalo del timer y la URI del namespace.
- **El logging usa `Console.WriteLine`** en lugar de Serilog. El stack además emite sus
  propias trazas internas por un canal (`ITelemetryContext`) que hoy no está conectado a
  nada.
- **Dos warnings de API obsoleta.** El del constructor de `ApplicationInstance` se
  resuelve al conectar Serilog; el de `AddSecurityConfiguration` pide construir una
  colección de identificadores de certificado, que es tema de la Fase 6.