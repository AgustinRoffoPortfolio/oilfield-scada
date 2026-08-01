-- Eventos de alarma: una fila por ocurrencia, no por tag.
-- El estado no se guarda como columna: se deduce de que marcas de tiempo estan cargadas.
--   raised_at, sin ack, sin clear  -> ACTIVA (sin reconocer)
--   con acked_at, sin cleared_at   -> RECONOCIDA (sigue fuera de rango)
--   con cleared_at, sin acked_at   -> NORMALIZADA sin reconocer (el operador nunca la vio)
--   con ambas                      -> CERRADA
-- Guardarlo asi evita que la columna de estado quede desincronizada de los tiempos.

CREATE TABLE alarm_events (
    alarm_id    BIGSERIAL PRIMARY KEY,
    tag_id      SMALLINT NOT NULL REFERENCES tags(tag_id),
    severity    TEXT NOT NULL CHECK (severity IN ('warn', 'alarm')),
    direction   TEXT NOT NULL CHECK (direction IN ('low', 'high')),

    limit_value DOUBLE PRECISION NOT NULL,  -- el umbral que se cruzo, copiado al momento del disparo
    raise_value DOUBLE PRECISION NOT NULL,  -- valor medido que la disparo

    raised_at   TIMESTAMPTZ NOT NULL,
    acked_at    TIMESTAMPTZ,
    cleared_at  TIMESTAMPTZ,
    clear_value DOUBLE PRECISION            -- valor con el que volvio a normal
);

-- Una sola alarma abierta por tag + severidad + lado. Si el valor oscila sobre el
-- limite, no se generan cien filas: se reusa la que ya esta abierta.
CREATE UNIQUE INDEX idx_alarm_open_unique
    ON alarm_events (tag_id, severity, direction)
    WHERE cleared_at IS NULL;

-- La consulta tipica del panel: las que siguen abiertas o sin reconocer, mas recientes primero.
CREATE INDEX idx_alarm_pending
    ON alarm_events (raised_at DESC)
    WHERE cleared_at IS NULL OR acked_at IS NULL;