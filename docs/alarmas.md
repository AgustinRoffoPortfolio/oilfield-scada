# Fase 5 — Motor de alarmas

## Qué hace esta fase

El dashboard de la Fase 4 ya pintaba de ámbar o magenta un valor fuera de rango, pero
ese color vivía y moría en la pantalla: si nadie estaba mirando a las 3 de la mañana,
nadie se enteraba. Esta fase convierte la condición anormal en un **evento persistente**
con ciclo de vida propio —aparece, se reconoce, se normaliza— de modo que quede
registrado quién vio qué y cuándo, y que el turno siguiente pueda revisar lo que pasó
mientras no había nadie.

## Decisiones de diseño

### 1. Proceso separado, no un módulo de la ingesta

El motor corre como una cuarta aplicación de consola (`src/Alarms`) que lee de la base,
en lugar de evaluar en memoria dentro de la ingesta justo antes de escribir.

La variante integrada era más eficiente: la ingesta ya tiene los valores frescos en
memoria y evaluar ahí no cuesta una consulta extra. Se descartó porque acopla el motor
al *camino* por el que entran los datos, y ese camino cambia en la Fase 6, cuando el
simulador salga a ser un esclavo Modbus TCP. Un motor que solo sabe leer últimos valores
de la base sobrevive a ese refactor sin tocarse.

El costo es una latencia de hasta el intervalo de evaluación (2 s) entre que el valor
cruza el umbral y la alarma existe. Para un yacimiento es irrelevante: los procesos de
producción tienen constantes de tiempo de minutos.

### 2. El estado se deriva de las marcas de tiempo

`alarm_events` no tiene columna de estado. Tiene tres timestamps —`raised_at`,
`acked_at`, `cleared_at`— y el estado se calcula de cuáles están cargados:

| raised | acked | cleared | Estado | Significado |
|---|---|---|---|---|
| ✓ | | | `active` | Fuera de rango, nadie la vio |
| ✓ | ✓ | | `acked` | Fuera de rango, el operador se hizo cargo |
| ✓ | | ✓ | `unacked_cleared` | Volvió a normal sin que nadie la viera |
| ✓ | ✓ | ✓ | `closed` | Cerrada |

Una columna de estado sería redundante con los timestamps y podría desincronizarse de
ellos: un `UPDATE` que escribe `cleared_at` pero olvida el estado deja la fila mintiendo.
Derivarlo hace que la contradicción sea imposible.

La tercera fila es la que justifica el diseño. Una alarma que se normalizó sola sigue
apareciendo en el panel hasta que alguien la reconozca, porque el hecho de que el pozo
se haya ido de rango y haya vuelto es información que el operador necesita aunque el
proceso ya esté bien.

### 3. Histéresis solo al retorno

El umbral de disparo es exacto; el de normalización se corre hacia adentro por el 2 %
del rango de ingeniería del tag. Un THP con escala 0–60 y alarma alta en 45 dispara al
llegar a 45,0 y normaliza recién al bajar de 43,8.

Sin ese margen, un valor oscilando sobre el límite genera decenas de alarmas por minuto,
y el operador aprende a ignorar el panel. Aplicarla solo al retorno —y no también al
disparo— es deliberado: la alarma tiene que sonar en el límite configurado, no un poco
después. El margen es para dejar de sonar, no para empezar.

El porcentaje se toma del rango de ingeniería y no de un valor absoluto, así el mismo
número sirve para una presión en bar y un caudal de gas en Nm³/d.

### 4. Una sola alarma por tag: la de mayor severidad

Cuando un valor cruza el límite de `alarm`, necesariamente ya cruzó el de `warn`. El
motor no deja las dos abiertas: cierra la de aviso y abre la de alarma, y al desescalar
hace lo inverso. El índice único parcial de la base lo garantiza:

```sql
CREATE UNIQUE INDEX idx_alarm_open_unique
    ON alarm_events (tag_id, severity, direction)
    WHERE cleared_at IS NULL;
```

Es lo que hace un SCADA real y evita que el panel muestre dos renglones diciendo lo
mismo del mismo tag. Se pierde el detalle de "primero fue aviso y a los 40 segundos
escaló", pero eso se reconstruye mirando la serie de medición, que es donde vive.

En el ciclo de aplicación **los cierres van antes que los disparos**, precisamente por
ese índice: si se insertara primero, la escalada chocaría contra la alarma de aviso
todavía abierta y se perdería.

### 5. El estado vive en la base, no en el motor

Cada ciclo el motor relee las alarmas abiertas en vez de mantenerlas en memoria. Si el
proceso se reinicia, retoma exactamente donde estaba: una alarma disparada hace una hora
sigue abierta y no se vuelve a disparar. Verificado apagando y levantando el motor con
una alarma activa.

### 6. Sin dato válido no se decide nada

Un tag no se evalúa si su última medición es más vieja que `StaleDataSeconds` (30 s) o
si su calidad no es `Good`. Y —esto es lo que importa— **una alarma ya abierta tampoco
se normaliza** en ese caso: sin dato actual no se puede afirmar que el valor volvió a
rango. Cerrarla por silencio sería el error opuesto y más peligroso: apagar una alarma
porque se cortó la comunicación.

### 7. El catálogo se relee en cada ciclo

Los umbrales se leen de `tags` en cada evaluación, no una sola vez al arrancar. Son 35
filas de una tabla chica; el costo es despreciable y a cambio un cambio de umbral en la
base tiene efecto sin reiniciar el motor. Con 15.000 tags (Fase 6) esto deja de ser
gratis y habría que releer por intervalo largo o por comando.

## Estructura del código

| Archivo | Responsabilidad |
|---|---|
| `src/Alarms/Program.cs` | Configuración, logger, y el loop de evaluación cada 2 s. |
| `src/Alarms/AlarmEngine.cs` | Decide qué disparar y qué normalizar. **No toca la base.** |
| `src/Alarms/AlarmRepository.cs` | Lectura de catálogo, últimos valores y alarmas abiertas; escritura de eventos. |
| `src/Alarms/AlarmModels.cs` | `TagLimits`, `LatestValue`, `OpenAlarm`. |
| `src/Alarms/AlarmOptions.cs` | Intervalo, porcentaje de histéresis, antigüedad máxima del dato. |
| `src/WebApp/AlarmRepository.cs` | Consultas del panel y comando de reconocimiento. |
| `src/WebApp/wwwroot/js/alarms.js` | Panel: tabla, botón de reconocer, refresco. |
| `sql/04_alarms.sql` | Tabla `alarm_events` e índices. |
| `tests/Alarms.Tests/` | 13 tests sobre el motor. |

Que `AlarmEngine` no toque la base es lo que hace testeable la fase: recibe el catálogo,
los últimos valores y las alarmas abiertas, y devuelve una lista de acciones. Los 13
tests cubren disparo en el límite exacto, no-rebote dentro del margen, normalización
pasado el margen, escalado y desescalado entre severidades, y los casos de dato viejo,
calidad mala y tag sin umbrales.

## API

| Endpoint | Devuelve |
|---|---|
| `GET /api/alarms` | Pendientes: sin normalizar, o normalizadas sin reconocer. |
| `GET /api/alarms/history?limit=` | Registro completo de eventos, más reciente primero. |
| `POST /api/alarms/{id}/ack` | Reconoce. `404` si no existe o ya estaba reconocida. |

El `ack` es idempotente: el `UPDATE` lleva `WHERE acked_at IS NULL`, así que reconocer
dos veces no cambia nada. Devolver `404` en el segundo caso mezcla "no existe" con "ya
estaba"; un API estricto distinguiría con `409`, pero a la UI le da igual y no se
justificó la complejidad.

## Panel

Ocupa la banda inferior del dashboard y reusa `js/state.js` —la misma clasificación y
los mismos colores que el mímico— en vez de definir los suyos. Una alarma ya normalizada
se muestra atenuada: sigue en pantalla porque nadie la reconoció, pero el proceso ya no
está mal.

El contador del encabezado solo se pinta si hay algo sin reconocer, siguiendo el criterio
ISA-101 del resto de la pantalla.

**El panel consulta cada 3 s, no usa SSE.** Es una decisión deliberada —polling para
eventos discretos, streaming para series continuas— pero convivir con dos mecanismos de
transporte es una inconsistencia visible. La versión prolija es un segundo broadcaster,
anotada en la Fase 8.

## Verificación

Con la cadena completa corriendo, se apretó el umbral de `POZO-A/ESP_vib` de 5,5 a
0,5 mm/s para forzar la condición sobre un valor de operación normal (~2,1 mm/s):

1. El motor disparó la alarma dentro del ciclo siguiente y la escribió en la base.
2. Se detuvo y relevantó el proceso: retomó la alarma abierta desde la base.
3. `GET /api/alarms` la devolvió como `active`; el panel la mostró en ámbar.
4. El botón de reconocer la pasó a `acked`, con `acked_at` cargado.
5. Devuelto el umbral a 5,5, el motor la normalizó **sin reiniciarse**, gracias a la
   relectura del catálogo.
6. Salió de `/api/alarms` y quedó como `closed` en `/api/alarms/history`.

## Los umbrales se aplican por trigger, no por UPDATE

Los límites viven en `tag_limit_defaults`, indexados por nombre de variable, y un
trigger `BEFORE INSERT` sobre `tags` se los copia a cada tag en el momento en que la
ingesta lo da de alta.

Antes eran una lista de `UPDATE ... WHERE variable = 'X'`, y eso escondía una
dependencia de orden: el esquema no crea los tags, los crea la ingesta al sincronizar
el address space. `scripts/start-all.ps1` aplica el esquema solo cuando la tabla
`tags` todavía no existe —o sea, siempre sobre una base vacía—, así que los UPDATE
corrían contra cero filas. Los tags nacían sin umbrales y el motor de alarmas
evaluaba 35 tags con todos los límites en NULL: arrancaba bien, no fallaba, y no
disparaba nunca. `GET /api/alarms` devolvía 200 con una lista vacía.

El síntoma solo aparecía en una base creada de cero, que es exactamente el camino de
alguien que clona el repo por primera vez.

Con el trigger, el orden deja de importar. Y además es la forma correcta de modelarlo:
los umbrales son propiedad del tipo de variable, no de la instancia — todo `THP` de
cualquier pozo comparte la banda. Si en el futuro un equipo nuevo trae una variable
con nombre repetido, se agrega el tipo de equipo a la clave de la tabla de defaults.

## Pendientes conocidos

- El panel consulta por polling en vez de SSE (Fase 8).
- El catálogo se relee entero en cada ciclo; no escala a 15.000 tags.
- `POST .../ack` no distingue "no existe" de "ya reconocida".
- No hay agrupamiento de alarmas por equipo ni supresión de alarmas derivadas: si un
  pozo falla, sus nueve variables pueden alarmar juntas. Un SCADA maduro tiene *alarm
  shelving* y correlación de causa raíz; queda fuera del alcance de este proyecto y es
  bueno saber dónde está el límite.