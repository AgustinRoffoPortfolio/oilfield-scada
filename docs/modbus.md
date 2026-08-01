# Fase 6 — Driver Modbus TCP

## Qué hace este paso

El simulador dejó de correr adentro del servidor OPC UA. Ahora es un proceso propio que
actúa como **RTU de campo**: expone una tabla de registros numerados por Modbus TCP, sin
nombres, sin unidades y sin jerarquía. El servidor OPC UA se conecta a él como maestro,
lee bloques de registros, los traduce a tags según el mapa del fabricante y los publica
en su árbol.

Es la arquitectura que se usa en campo: el equipo habla su protocolo nativo, un driver lo
traduce, y el servidor OPC UA expone el resultado con semántica. Es también el proyecto de
migración que tienen pendiente miles de instalaciones reales — Modbus sigue en todos lados
y OPC UA es a dónde se está yendo.

## El problema central: un double no entra en 16 bits

Un registro Modbus son 16 bits sin significado propio. Una medición es un número real.
Hay dos formas de resolverlo y las dos se usan:

**Entero escalado.** Cada tag lleva un factor: la vibración viaja en centésimas de mm/s,
la presión en décimas de bar. Un registro por tag. Es lo que hacen los equipos más viejos.
Desventaja: el factor es configuración por tag, y si el rango se queda corto el valor
satura sin avisar.

**Float de 32 bits en dos registros.** El valor crudo partido en dos palabras de 16 bits.
No hay factor que elegir ni rango que saturar. **Es la opción elegida acá.**

Su desventaja es la parte interesante: Modbus define big-endian *dentro* de cada registro,
pero **nunca estandarizó cuál de las dos palabras va primero**. Existen las cuatro
combinaciones en el mercado. Leer con el orden equivocado no da error — da un número
absurdo, o peor, un número plausible. Es el clásico "me da 3,4e38, probá invirtiendo las
palabras".

Por eso el orden es **configuración explícita** (`wordOrder` en el mapa) y no una
constante enterrada en el código. La conversión vive en `Shared/ModbusFloat.cs` y la usan
los dos extremos, así que ida y vuelta son necesariamente simétricas. Trece tests en
`ModbusFloatTests` cubren el round-trip en ambos órdenes, incluidos los extremos del tipo,
y uno documenta explícitamente el síntoma de leer con el orden cambiado.

## El mapa del fabricante

`config/modbusmap.json` es el equivalente a la tabla que un fabricante publica en su
manual. **No se genera desde el modelo: se transcribe y se valida.** Generarlo
automáticamente habría sido más cómodo y menos realista — en campo el mapa te lo dan y vos
te adaptás.

Estructura: bloques de 20 registros por equipo (0, 100, 200, 300, 400), cada float
ocupando dos direcciones consecutivas, y `Status` en un solo registro por ser un entero
que entra en 16 bits. Los pozos usan 19 de sus 20, dejando el hueco típico para crecer.

`ModbusMap.Validate()` rechaza al cargar dos errores caros:

- **Direcciones pisadas.** Si dos mediciones comparten un registro no falla nada en
  runtime: simplemente leés el valor de otra cosa.
- **Entradas fuera de su bloque.** Una dirección mal tipeada que cae en el bloque del
  equipo vecino.

Además, `CrossCheck()` compara el mapa contra el address space al arrancar el servidor y
avisa de tags sin registro y de registros sin tag. Son dos archivos que mantiene un
humano; el desfasaje se detecta al arrancar, no cuando falta un valor en la pantalla.

## El protocolo, escrito a mano

Sin paquete NuGet, por coherencia con la regla del frontend y porque el protocolo entero
que este proyecto necesita entra en unas pocas decenas de líneas.

Un pedido son 12 bytes: identificador de transacción (2), protocolo (2, siempre cero),
longitud (2), unidad (1), función (1), dirección de arranque (2) y cantidad (2). La
respuesta repite la cabecera, la función, un contador de bytes y los registros crudos.
Está implementada una sola función, la 3 (*Read Holding Registers*): este RTU publica
mediciones, no recibe órdenes.

El contraste con OPC UA es el argumento del proyecto en una imagen. Modbus no tiene
descubrimiento, ni tipos, ni unidades, ni suscripciones, ni sesión: son números en
direcciones y el significado vive en un PDF. OPC UA tiene todo eso. Por eso Modbus se
escribe en una tarde y OPC UA necesita un stack certificado — y por eso la industria
sigue migrando de uno al otro.

### Tres trampas del protocolo sobre TCP

**TCP es un flujo, no mensajes.** Pedir 12 bytes puede devolver 5. Ambos extremos usan un
`ReadExactly` que insiste hasta completar el buffer. Es el primer bug de todo el que
escribe su primer protocolo sobre sockets.

**El identificador de transacción no es decorativo.** Si una respuesta llega tarde y la
siguiente lectura la toma como propia, todos los valores quedan corridos un ciclo. No da
error: da datos viejos. El driver verifica que el identificador de la respuesta sea el que
mandó.

**Un socket muerto no avisa, deja de contestar.** `ReceiveTimeout` de `TcpClient` no
aplica a `ReadAsync`, así que sin un `CancellationTokenSource` con timeout el driver espera
para siempre. Este bug apareció durante el desarrollo: el driver conectaba bien al arrancar
pero nunca reconectaba, y el log mostraba un intento de conexión que jamás volvía. En campo
sería un valor congelado en pantalla que nadie sabe que está congelado.

## Pérdida de comunicación, de verdad

La falla "pérdida de comunicación" dejó de ser simulada. Se corta el proceso del RTU y el
driver pierde el socket real: marca todos los tags como sin dato, el `NodeManager` los
publica con `BadNoCommunication`, y el mímico lo refleja. Al volver a levantar el RTU, el
driver reconecta solo en el intervalo configurado.

Detalle deliberado: al caer la comunicación, los valores se **descartan**, no se conservan.
Un valor viejo publicado como bueno es peor que ningún valor — el operador no tiene forma
de saber que está mirando algo congelado.

Las propiedades del nodo (`EngineeringUnits`, `EURange`, `EnumStrings`) siguen en `Good`
durante el corte, y eso es correcto: son metadatos del tag, no lecturas de campo. Un tag
sigue midiendo bar en el rango 0–60 aunque el cable esté cortado, y eso permite que la
pantalla siga dibujando la escala del indicador con el valor tachado en vez de quedarse en
blanco.

## El corte que hizo posible todo esto

El servidor OPC UA no cambió una línea para pasar del simulador en proceso al RTU remoto.
La razón es la interfaz `ITagValueSource`, introducida en el paso anterior con un solo
método: dame el valor de `POZO-A/THP`. `SimulatorTagSource` la implementaba consultando
objetos en memoria; `ModbusTagSource` la implementa consultando registros por un socket.
El `NodeManager` no sabe cuál está del otro lado.

El manejo de la caída tampoco se escribió para esto: el `NodeManager` ya publicaba
`BadNoCommunication` cuando la fuente respondía `false`, porque esa era la respuesta para
un tag configurado sin fuente de datos. La misma rama de código cubre los dos casos.

## Decisiones menores

- **Puerto 5502, no 502.** En Windows los puertos bajo 1024 exigen permisos elevados. Es
  configuración del cliente, no una desviación del protocolo.
- **La lectura corre en su propia tarea**, no en el ciclo del servidor: un RTU lento o
  caído no debe frenar la publicación de los nodos. `Step()` de `ModbusTagSource` está
  vacío a propósito.
- **Un pedido por equipo**, cubriendo desde el primer registro hasta el último del bloque
  aunque haya huecos. Lo caro es el ida y vuelta, no los bytes: 19 registros cuestan lo
  mismo que 2.
- **La caída se loguea una vez**, no en cada reintento. Un RTU apagado no debe llenar el
  log de ruido.

## Verificación

- Cliente Modbus manual en PowerShell contra el RTU: respuesta
  `00 01 00 00 00 07 01 03 04 42 06 E0 F7`, que decodifica a 33,72 bar y coincide con el
  THP que muestra el cuadro del simulador.
- Cadena completa de cinco procesos: degradación de bomba inyectada por teclado en el RTU,
  visible en el mímico y disparando alarma de vibración en el dashboard.
- Corte y reconexión del RTU con la cadena corriendo: 35 tags a `BadNoCommunication` y
  vuelta a `Good` sin intervención.

## Pendiente

- `src/Simulator` es hoy el RTU. El driver vive todavía en `src/OpcUaServer/`; moverlo a
  `src/ModbusDriver/` cuando crezca.
- `app.js` deriva los grupos de un `startsWith("POZO")` hardcodeado; deben salir del campo
  `equipment` que ya viene en los datos.