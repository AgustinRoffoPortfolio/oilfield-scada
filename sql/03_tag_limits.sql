-- Umbrales operativos por tag. NULL = sin limite definido de ese lado.
-- warn: fuera de la banda normal de operacion. alarm: condicion critica.
-- Son configuracion de planta, no de la pantalla: por eso viven en la base.
--
-- Los limites son del TIPO de variable, no de la instancia: el THP de cualquier pozo
-- comparte la misma banda. Por eso viven en una tabla de defaults y un trigger los
-- copia a cada tag en el momento en que la ingesta lo da de alta.
--
-- Antes esto era una lista suelta de UPDATE, y tenia un problema de orden: los tags
-- no los crea el esquema, los crea la ingesta al sincronizar el address space. Si el
-- archivo se aplicaba sobre una base vacia --que es exactamente lo que hace
-- scripts/start-all.ps1 en el primer arranque-- los UPDATE corrian contra cero filas,
-- los tags nacian sin umbrales y el motor de alarmas se quedaba mudo para siempre.
-- Con el trigger, da igual el orden.

ALTER TABLE tags
    ADD COLUMN IF NOT EXISTS warn_low    DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS warn_high   DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS alarm_low   DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS alarm_high  DOUBLE PRECISION;

-- --- Catalogo de limites por nombre de variable ------------------------------

CREATE TABLE IF NOT EXISTS tag_limit_defaults (
    variable    TEXT PRIMARY KEY,
    warn_low    DOUBLE PRECISION,
    warn_high   DOUBLE PRECISION,
    alarm_low   DOUBLE PRECISION,
    alarm_high  DOUBLE PRECISION
);

-- El ON CONFLICT hace que reaplicar el archivo actualice los valores: este archivo
-- es la fuente de verdad de los umbrales.
INSERT INTO tag_limit_defaults (variable, warn_low, warn_high, alarm_low, alarm_high) VALUES
    -- Pozos: la banda normal sale de la tabla de dominio del proyecto.
    ('THP',           15,     40,     10,     45),
    ('CHP',            8,     25,      5,     30),
    ('T_head',        40,     95,     30,    105),
    -- 120 es el rango nominal, pero POZO-C opera cerca de 146: el limite va con
    -- margen sobre lo que el proceso hace de verdad, o la operacion normal alarma.
    ('Q_oil',          5,    160,      2,    190),
    ('Q_water',     NULL,    200,   NULL,    240),
    ('Q_gas',        500,  20000,   NULL,   NULL),
    ('ESP_current',   20,     75,   NULL,     85),
    ('ESP_freq',      35,     60,   NULL,   NULL),
    -- La vibracion no tiene limite bajo: cuanto menos, mejor.
    ('ESP_vib',     NULL,    5.5,   NULL,    7.0),
    -- Separador y ducto.
    ('Sep_P',          6,     14,      4,     18),
    ('Sep_level',     25,     75,     15,     85),
    ('Pipe_P_in',      6,     14,   NULL,     18),
    ('Pipe_P_out',     2,      8,   NULL,   NULL),
    ('Pipe_Q',        50,    600,   NULL,   NULL)
ON CONFLICT (variable) DO UPDATE SET
    warn_low   = EXCLUDED.warn_low,
    warn_high  = EXCLUDED.warn_high,
    alarm_low  = EXCLUDED.alarm_low,
    alarm_high = EXCLUDED.alarm_high;

-- --- Trigger: aplicar los defaults a cada tag nuevo --------------------------

-- COALESCE y no asignacion directa: si algun dia alguien inserta un tag con umbrales
-- propios, el default no se los pisa. Y si la variable no esta en el catalogo
-- (Status, por ejemplo, que es un enum y no se compara contra umbrales), la fila
-- queda como venia.
CREATE OR REPLACE FUNCTION apply_tag_limit_defaults()
RETURNS TRIGGER AS $$
DECLARE
    d tag_limit_defaults%ROWTYPE;
BEGIN
    SELECT * INTO d FROM tag_limit_defaults WHERE variable = NEW.variable;
    IF FOUND THEN
        NEW.warn_low   := COALESCE(NEW.warn_low,   d.warn_low);
        NEW.warn_high  := COALESCE(NEW.warn_high,  d.warn_high);
        NEW.alarm_low  := COALESCE(NEW.alarm_low,  d.alarm_low);
        NEW.alarm_high := COALESCE(NEW.alarm_high, d.alarm_high);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_tag_limit_defaults ON tags;
CREATE TRIGGER trg_tag_limit_defaults
    BEFORE INSERT ON tags
    FOR EACH ROW
    EXECUTE FUNCTION apply_tag_limit_defaults();

-- --- Tags que ya existen -----------------------------------------------------

-- El trigger solo dispara en inserciones nuevas, asi que los tags ya dados de alta
-- necesitan el UPDATE explicito. Es lo que hacia el archivo entero antes.
UPDATE tags t
   SET warn_low   = d.warn_low,
       warn_high  = d.warn_high,
       alarm_low  = d.alarm_low,
       alarm_high = d.alarm_high
  FROM tag_limit_defaults d
 WHERE d.variable = t.variable;