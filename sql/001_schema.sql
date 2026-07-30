-- Catálogo de tags: una fila por variable medida.
CREATE TABLE tags (
    tag_id     SMALLSERIAL PRIMARY KEY,
    name       TEXT NOT NULL UNIQUE,   -- 'POZO-A/THP', igual al NodeId de OPC UA
    equipment  TEXT NOT NULL,          -- 'POZO-A'
    variable   TEXT NOT NULL,          -- 'THP'
    unit       TEXT,                   -- 'bar'
    eu_min     DOUBLE PRECISION,       -- rango de ingeniería, para escalar gráficos
    eu_max     DOUBLE PRECISION
);

-- Serie temporal: crece permanentemente.
CREATE TABLE measurements (
    ts       TIMESTAMPTZ NOT NULL,
    tag_id   SMALLINT NOT NULL REFERENCES tags(tag_id),
    value    DOUBLE PRECISION NOT NULL,
    quality  SMALLINT NOT NULL DEFAULT 0   -- 0=Good, 1=Uncertain, 2=Bad
);

-- Convertir en hypertable: particionada por tiempo, un chunk por día.
SELECT create_hypertable('measurements', by_range('ts', INTERVAL '1 day'));

-- Índice para la consulta típica: un tag, ordenado por tiempo descendente.
CREATE UNIQUE INDEX idx_measurements_tag_ts ON measurements (tag_id, ts DESC);