# Fase 6 — Address space configurable

## Qué hace este paso

Hasta acá, qué tags existía en el yacimiento estaba escrito en C# y compilado dentro
del ejecutable: agregar un pozo era editar `WellTagCatalog.cs`, recompilar y desplegar.
Un servidor OPC UA real no funciona así — el integrador que lo instala en un yacimiento
no es el que lo programó, y no tiene por qué tener el código fuente. Este paso mueve
esa definición a un archivo de configuración, `config/addressspace.json`, del que el
servidor arma su árbol al arrancar.

## Decisiones de diseño

### 1. Una sola fuente de verdad, no dos

El archivo alimenta **tanto** el árbol de nodos OPC UA **como** la tabla `tags` de la
base, vía `FieldTagCatalog.Build()`. La alternativa —dejar el JSON solo del lado del
servidor y que la ingesta siga derivando su catálogo del modelo compilado— era menos
invasiva, pero creaba dos definiciones que podían divergir en silencio: se agrega un
tag al archivo, aparece en OPC UA, y la ingesta no lo guarda porque no está en su
lista. Ese tipo de desincronización no da error, da un hueco en el historial que se
descubre semanas después.

### 2. Un tag es enum si declara estados, y analógico si no

No hay campo `"type"` en el archivo. Un tag con `states` se crea como
`MultiStateDiscreteState`; sin `states`, como `AnalogItemState` con unidad y rango.
La alternativa era un discriminador explícito, que agrega una cosa más que puede quedar
inconsistente con el resto de la fila (`type: "analog"` junto a un `states`). Acá la
forma del dato **es** la declaración de tipo.

### 3. El servidor no conoce el dominio

`OilfieldNodeManager` ya no sabe que un pozo tiene una bomba ni que `THP` es la presión
de boca: recorre tipos, equipos y tags. Todo el conocimiento del dominio quedó en una
sola clase, `SimulatorTagSource`, detrás de la interfaz `ITagValueSource`, que expone
un único método: dame el valor de `POZO-A/THP`. Ese corte es deliberado y es la
preparación del paso siguiente — cuando el simulador salga a ser un esclavo Modbus, se
reemplaza esa clase y el resto del servidor no se entera.

### 4. Un archivo, no una copia por aplicación

`AddressSpaceConfig.Resolve()` busca el archivo subiendo directorios desde la carpeta
de salida hasta encontrarlo. Cada aplicación corre desde su propio `bin/Debug/net10.0`,
así que la alternativa habitual —copiar el JSON a la salida en el build— habría dejado
tres copias que hay que mantener sincronizadas a mano. La configuración del campo es
una sola cosa y vive en un solo lugar.

### 5. Falla temprano y con nombre propio

`Validate()` corre al cargar y rechaza tipos o equipos duplicados, tags sin rango,
`low >= high` y equipos que referencian un tipo inexistente, siempre nombrando el
archivo y el tag concreto. Un error de configuración tiene que doler al arrancar, no
manifestarse tres horas después como un valor raro en una pantalla.

## Tags configurados sin fuente de datos

Si el archivo declara un equipo que la fuente de datos no conoce, el servidor **no
falla**: crea los nodos, los publica con calidad `BadNoCommunication` y avisa al
arrancar cuántos tags quedaron huérfanos y cuáles. Es el comportamiento correcto para
campo, donde la configuración suele adelantarse a la instalación física: el tag ya
existe en el sistema, marcado como sin comunicación, y el día que el equipo se conecta
empieza a reportar sin tocar nada.

Verificado agregando un `POZO-D` al archivo: el árbol pasó a 45 tags, la ingesta
sincronizó 45 en la base, la suscripción tomó los 45 items y los 10 del pozo
inexistente no escribieron ni una fila de medición.

## Cómo agregar un equipo

Editar `config/addressspace.json` y reiniciar el servidor y la ingesta. Nada más.

- **Equipo de un tipo que ya existe:** una línea en `devices`.
- **Equipo de un tipo nuevo:** una entrada en `types` con sus tags, más la línea en
  `devices`.
- **Alimentarlo con datos:** hoy, agregarlo a `SimulatorTagSource`. Cuando entre el
  driver Modbus, mapearlo a registros.

La tabla `tags` se sincroniza sola al arrancar la ingesta. Los tags que se sacan del
archivo **no** se borran de la base: un historian no tira historia porque un equipo
salió de servicio.

## Escala

Con el catálogo compilado, el benchmark de 15.000 tags de esta misma fase habría
requerido generar código. Ahora es escribir un archivo.