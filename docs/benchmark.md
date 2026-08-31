# Benchmark de escala

Mide cómo se comporta la cadena completa —servidor OPC UA, ingesta y base de
datos— al pasar de los 35 tags del campo simulado a 15.000.

El objetivo declarado fue **medir, no optimizar**. Los cuellos de botella que
aparecieron se documentan como hallazgos, con su explicación; no se corrigieron.
Saber dónde están los límites vale más que esconderlos.

---

## Resultados

| Escenario | Tags activos | Filas/s | Latencia prom (ms) | Latencia p95 (ms) | Últimos valores (ms) | Disco (GB/día) |
|---|---|---|---|---|---|---|
| Campo real | 35 | 9,8 | 915 | 1.166 | 0,3 | 0,11 |
| 500 | 505 | 346,3 | 875 | 885 | 3,2 | 3,02 |
| 5.000 | 5.005 | 4.351,7 | 221 | 246 | 30,5 | 40,88 |
| 15.000 | 13.508 | 13.079,7 | 536 | 587 | 121,1 | 119,23 |

Ninguna corrida perdió datos, se reconectó ni acumuló atraso en el volcado. La
única falla de la serie fue el agotamiento de la secuencia de `tag_id`, que es un
hallazgo del esquema y no de la carga: está en el punto 5.

La columna de últimos valores mide la consulta que el sistema ejecuta de verdad.
Una versión anterior de esta tabla medía una consulta distinta, más simple, que
ningún componente del proyecto corre; el detalle está en *Origen de cada métrica*.

---

## Cómo se midió

Cada escenario corrió **5 minutos**, precedidos de 30 segundos de estabilización
que se descartan: el arranque incluye la primera lectura de todos los items
suscriptos, que es un pico no representativo del régimen estacionario.

El script `scripts/run-benchmark.ps1` hace la corrida completa sin intervención
manual: trunca las tablas, levanta los cinco procesos, espera, consulta la base y
baja todo. La reproducibilidad importa más que la comodidad — una medición hecha
a ojo, leyendo logs y cronometrando a mano, no es comparable entre escenarios.

Los address spaces sintéticos los genera `scripts/gen-bench-addressspace.ps1`,
que toma el campo real y le agrega pozos clonados del mismo `WellType` hasta
llegar al total pedido. Los archivos generados no se versionan: se reproducen con
el script.

**Origen de cada métrica:**

- **Filas/s** — conteo de filas en `measurements` dentro de la ventana medida,
  dividido por su duración real.
- **Latencia** — desde el `SourceTimestamp` que pone el servidor OPC UA hasta
  que la fila se escribe. La ingesta la registra en cada volcado (promedio, p95 y
  máximo) y el script promedia esas muestras.
- **Últimos valores** — `EXPLAIN ANALYZE` sobre la consulta de producción, copiada
  literal de `ReadingRepository.LatestSql`: el `LATERAL` contra el catálogo que
  alimenta tanto `/api/tags/latest` como el ciclo de un segundo del stream SSE. En
  la misma corrida se mide al lado la alternativa descartada en la Fase 4
  (`DISTINCT ON` sin join), que no va a la tabla pero sostiene el hallazgo 3.
- **Disco** — tamaño real de la hypertable dividido por su cantidad de filas, y
  proyectado a un día a la tasa de escritura medida.

### Máquina de prueba

| | |
|---|---|
| CPU | Intel Core i5-12450H (8 núcleos, 12 hilos) |
| RAM | 7,7 GB |
| Disco | NVMe Micron 3400, 512 GB |
| SO | Windows 11 25H2 (build 26200) |
| Base de datos | TimescaleDB 2.29 en Docker sobre WSL2 |

Todo corrió en esa única máquina: los cinco procesos de la aplicación y la base
compitiendo por los mismos recursos, sin red de por medio y sin dimensionamiento.

Dos aclaraciones que hacen que estos números sean un **piso y no un techo**:

Los 7,7 GB de RAM son poco para esta carga. Postgres tuvo margen muy escaso para
cachear, y la memoria se repartió además entre las cinco aplicaciones .NET y la
máquina virtual de WSL2. Con más RAM disponible para *shared buffers*, la tasa de
escritura sostenida debería mejorar.

La base corre en Docker sobre WSL2, no nativa. Eso agrega una capa de
virtualización del sistema de archivos entre Postgres y el NVMe, con un costo de
E/S medible en Windows. El mismo hardware con Postgres nativo daría mejores
números.

---

## Hallazgos

### 1. La latencia tiene un mínimo, y no está en el campo chico

La latencia no crece con la carga: baja, toca fondo y vuelve a subir. De 915 ms
con 35 tags a 875 con 505, se desploma a **221 ms con 5.005** y repunta a 536 con
13.508.

La mitad izquierda de esa curva la explica el volcado. La ingesta escribe por
lotes cada 2 segundos, así que un dato que llega justo después de un volcado
espera hasta el siguiente. Con pocos tags y deadband activo, muchos ciclos
encuentran la cola casi vacía y la espera es todo lo que se mide: para datos que
llegan repartidos al azar dentro del intervalo, el promedio teórico es de
1.000 ms, y los 915 y 875 de los dos escenarios chicos están justo ahí. No es
lentitud del sistema, es el costo fijo del intervalo sin nada que lo amortice.

La mitad derecha es el volumen. A 13.508 tags el lote de cada volcado es enorme y
escribirlo empieza a costar tiempo propio, así que la latencia vuelve a subir. En
el medio, alrededor de 5.000 tags, hay suficiente tráfico para que ningún dato
espere de más y todavía poco para que el lote pese: es el punto óptimo de esta
configuración.

La consecuencia práctica es que **bajar la latencia en campos chicos no requiere
más capacidad sino un volcado más frecuente**, que es un parámetro y no un
rediseño.

El otro efecto visible es que la carga estabiliza. La distancia entre promedio y
máximo se achica: 2,1 veces en los dos escenarios chicos (915/1.922 y 875/1.858)
contra 1,3 y 1,4 en los grandes (221/292 y 536/734). Bajo carga constante el
sistema entra en régimen y deja de tener picos.

### 2. La escritura escala linealmente

De 505 a 5.005 tags (×9,9) las filas por segundo pasaron de 346 a 4.352 (×12,6).
De ahí a 13.508 activos (×2,7), llegaron a 13.080 (×3,0). No hay degradación: la
escritura por lotes con COPY binario absorbe el crecimiento sin curvarse.

A 15.000 tags el sistema entrega **casi exactamente una fila por tag por segundo**
(0,97), que es el techo teórico dado el ciclo de actualización de 1 segundo del
servidor. En otras palabras, a esa escala la ingesta ya no está filtrando nada y
sigue sin atrasarse: está en el máximo que la fuente puede producir.

### 3. La consulta de últimos valores: la elección correcta depende del régimen

Es el hallazgo más interesante de la serie, porque contradice una decisión de
diseño tomada dos fases antes — y explica por qué la contradice.

En la Fase 4 se eligió `LATERAL` midiendo contra 6.048.823 filas repartidas en 6
chunks, con los 35 tags del campo real: 0,5 ms contra 11,7 del `DISTINCT ON` sin
join y 2.491 del `DISTINCT ON` con join. La ventaja era estructural: el
`ChunkAppend` de TimescaleDB encuentra el dato en el chunk más nuevo y deja los
otros cinco en `never executed`, así que el costo no crece con el histórico
acumulado.

Ese régimen —pocos tags, mucho histórico— es el opuesto al del escenario grande. A
13.508 tags, `LATERAL` **pierde**:

| Tags activos | `LATERAL` (producción) | `DISTINCT ON` sin join | Relación |
|---|---|---|---|
| 505 | 3,2 ms | 2,3 ms | 1,4× |
| 5.005 | 30,5 ms | 20,8 ms | 1,5× |
| 13.508 | 121,1 ms | 65,6 ms | 1,8× |

La brecha no solo existe: se ensancha. La causa está anotada como limitación
conocida en la Fase 4, un año antes de medirla — *"LATERAL es rápido porque todos
los tags reportan seguido; un tag sin datos recientes obligaría a bajar chunk por
chunk hasta encontrarlo"*. En el escenario grande hay 1.497 tags `Status` que
nunca reciben valor, y cada uno de ellos recorre la hypertable entera para no
encontrar nada. El modo de falla estaba previsto; el benchmark lo confirmó.

**Pero cambiar de consulta no es la conclusión.** El `DISTINCT ON` medido devuelve
`tag_id`, `ts`, `value` y `quality`, y nada más. El dashboard necesita además el
nombre, el equipo, la unidad, el rango de ingeniería y los cuatro umbrales, todo
del catálogo — y agregar ese join es exactamente lo que en la Fase 4 explotó a
2.491 ms, porque el planificador descarta el `SkipScan` sin aviso y pasa a leer
todas las filas. El número de la tabla es un **piso teórico de la alternativa, no
un reemplazo directo**.

La lectura correcta es que ninguna de las dos formas sirve para los dos regímenes,
y que a esta escala el camino es el que ya estaba identificado: **una tabla de
últimos valores mantenida por la ingesta**, que no recalcula nada y es indiferente
tanto a la cantidad de tags como al histórico. No se implementó: excede el objetivo
de esta medición.

Importa más de lo que sugiere el número absoluto, porque es la consulta que corre
el stream SSE **una vez por segundo**. A 121 ms está usando el 12% de su ciclo, con
margen todavía, pero es la primera pieza que se rompe si el campo crece otro orden
de magnitud.

### 4. El crecimiento en disco justifica la compresión

119 GB por día a 13.508 tags son unos 3,6 TB por mes sin compresión. La corrida de
5 minutos dejó 444 MB.

Es el argumento concreto detrás del esquema largo (`ts`, `tag_id`, `value`,
`quality`): ese formato es el que la compresión por columnas de TimescaleDB
comprime bien, porque agrupa valores del mismo tipo y de rango acotado. Sin
compresión ni política de retención —que hoy no están configuradas— el esquema
largo paga su costo sin cobrar su beneficio.

### 5. El esquema tiene un techo duro de 32.767 tags

El primer intento del escenario grande no escribió una sola fila. La ingesta murió
a los tres segundos de arrancar:

```
2200H: nextval: reached maximum value of sequence "tags_tag_id_seq" (32767)
```

`tag_id` es un `SMALLINT`, así que la secuencia que lo genera se agota en 32.767.
Y se agota más rápido de lo que parece: la sincronización del catálogo usa
`INSERT ... ON CONFLICT (name) DO NOTHING`, y Postgres evalúa el `nextval` **antes**
de detectar el conflicto. Cada corrida del escenario grande quema 15.005 valores de
la secuencia aunque no inserte una sola fila nueva. Tres o cuatro corridas y se
llegó al techo.

Se resolvió en el script, que ahora trunca `tags` con `RESTART IDENTITY CASCADE`
junto con las tablas de datos. Pero el límite del esquema sigue ahí, y conviene
decirlo en voz alta: **este proyecto mide a 13.508 tags, que es el 41% del máximo
que la tabla puede identificar.** Un campo de 40.000 tags no entra sin migrar la
columna a `INT`.

El `SMALLINT` no fue una mala elección —dos bytes por fila, multiplicados por miles
de millones de filas, es parte del argumento del esquema largo— pero es una
decisión con un techo, y un techo que no está documentado es una bomba de tiempo.
Migrar a `INT` cuesta dos bytes por fila en `measurements`, que es donde duele.

---

## Limitaciones declaradas

Estas son deliberadas y acotan qué significa la tabla.

**Los valores sintéticos no tienen modelo físico.** Los tags de los pozos
generados se rellenan con una señal senoidal desfasada dentro de su rango de
ingeniería (`BenchTagSource`). El benchmark mide el transporte y el
almacenamiento, no la fidelidad del proceso. Los 35 tags del campo real sí
mantienen su modelo y su recorrido por Modbus.

**Se midieron 13.508 tags activos, no 15.005.** El generador sintético solo
alimenta tags analógicos, porque un enum no tiene rango del que derivar una
señal. Los 1.497 `Status` de los pozos generados existen en el árbol OPC UA pero
nunca reciben valor. La cifra de la tabla es la real.

**Esos 1.497 tags mudos encarecen la consulta de últimos valores.** Como se explica
en el hallazgo 3, `LATERAL` paga un recorrido completo de la hypertable por cada
tag sin datos. Un campo real donde todos los tags reportan daría un número mejor
que los 121 ms medidos: en esa columna, el escenario grande es un caso pesimista.

**La latencia es de ingesta, no de punta a punta hasta la pantalla.** Cubre desde
que el servidor genera el valor hasta que la fila queda escrita. No incluye el
tramo del SSE ni el renderizado del dashboard. El nombre correcto de lo medido es
*latencia de ingesta*.

**El deadband apenas filtra bajo señal sintética.** La senoidal se mueve más que
el 0,2% configurado en cada ciclo, así que casi todos los cambios se reportan. Un
campo real, con variables que se quedan quietas durante minutos, produciría
bastante menos tráfico a la misma cantidad de tags. Los números de esta tabla son
un caso pesimista en ese sentido.

**Las cuatro corridas son de una sola sesión, sin repeticiones.** No hay
desviación estándar ni intervalos de confianza: cada celda es una medición. Las
columnas de escritura y disco reprodujeron una serie anterior casi exactamente
(346,3 contra 346,3 filas/s; 3,02 contra 3,04 GB/día), lo que da confianza en esa
mitad de la tabla. La columna de latencia, en cambio, se movió bastante entre
sesiones, así que conviene leerla por su forma —dónde está el mínimo y por qué—
más que por sus valores absolutos.