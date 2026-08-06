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
| Campo real | 35 | 9,9 | 606 | 885 | 0,2 | 0,12 |
| 500 | 505 | 346,3 | 238 | 244 | 1,9 | 3,04 |
| 5.000 | 5.005 | 4.365,8 | 404 | 430 | 25,3 | 40,65 |
| 15.000 | 13.508 | 13.044,7 | 682 | 723 | 60,0 | 119,64 |

Ninguna corrida falló. A 15.000 tags el sistema siguió entregando datos sin
pérdida, sin reconexiones y sin atrasos acumulados en el volcado.

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
- **Últimos valores** — `EXPLAIN ANALYZE` sobre el `DISTINCT ON` que alimenta el
  dashboard.
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

### 1. La latencia está dominada por el intervalo de volcado, no por la carga

El resultado más contraintuitivo: **el campo real de 35 tags tiene peor latencia
que el escenario de 500** (606 ms contra 238). No es ruido.

Con pocos tags y deadband activo, muchos ciclos del volcado encuentran la cola
casi vacía. Un dato que llega justo después de un volcado espera hasta el
siguiente, y el temporizador corre cada 2 segundos. Con 500 tags siempre hay algo
para escribir, así que ningún dato espera de más.

O sea que la línea de base **no es el mejor caso**: es el caso donde el costo
fijo del intervalo pesa más. Bajar la latencia en campos chicos no requiere más
capacidad sino un volcado más frecuente, que es un parámetro, no un rediseño.

El otro efecto visible es que **la carga estabiliza**. La distancia entre
promedio y máximo se achica al crecer: 1.282 ms de máximo con 500 tags contra 807
con 15.000. Bajo carga constante el sistema entra en régimen y deja de tener
picos.

### 2. La escritura escala linealmente

De 505 a 5.005 tags (×10) las filas por segundo pasaron de 346 a 4.366 (×12,6).
De ahí a 13.508 activos (×2,7), llegaron a 13.045 (×3,0). No hay degradación: la
escritura por lotes con COPY binario absorbe el crecimiento sin curvarse.

A 15.000 tags el sistema entrega **casi exactamente una fila por tag por
segundo**, que es el techo teórico dado el ciclo de actualización de 1 segundo
del servidor. En otras palabras, a esa escala la ingesta ya no está filtrando
nada y sigue sin atrasarse: está en el máximo que la fuente puede producir.

### 3. La consulta de últimos valores es el primer cuello

Es la métrica que peor escala: 0,2 → 1,9 → 25,3 → 60,0 ms. Sigue siendo
utilizable, pero creció 300 veces mientras la latencia de ingesta apenas se
movió.

Importa más de lo que sugiere el número absoluto, porque **es la consulta que
corre el stream SSE en cada ciclo** para alimentar el dashboard. A 60 ms por
consulta con un solo consultador compartido todavía sobra margen, pero es la
primera pieza que habría que atacar si el campo creciera otro orden de magnitud.

El camino conocido es una tabla de últimos valores mantenida por trigger o por la
propia ingesta, en lugar de recalcular el `DISTINCT ON` sobre el historial
completo. No se implementó: excede el objetivo de esta medición.

### 4. El crecimiento en disco justifica la compresión

120 GB por día a 15.000 tags son unos 3,6 TB por mes sin compresión. La corrida
de 5 minutos dejó 467 MB.

Es el argumento concreto detrás del esquema largo (`ts`, `tag_id`, `value`,
`quality`): ese formato es el que la compresión por columnas de TimescaleDB
comprime bien, porque agrupa valores del mismo tipo y de rango acotado. Sin
compresión ni política de retención —que hoy no están configuradas— el esquema
largo paga su costo sin cobrar su beneficio.

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

**La latencia es de ingesta, no de punta a punta hasta la pantalla.** Cubre desde
que el servidor genera el valor hasta que la fila queda escrita. No incluye el
tramo del SSE ni el renderizado del dashboard. El nombre correcto de lo medido es
*latencia de ingesta*.

**El deadband apenas filtra bajo señal sintética.** La senoidal se mueve más que
el 0,2% configurado en cada ciclo, así que casi todos los cambios se reportan. Un
campo real, con variables que se quedan quietas durante minutos, produciría
bastante menos tráfico a la misma cantidad de tags. Los números de esta tabla son
un caso pesimista en ese sentido.
