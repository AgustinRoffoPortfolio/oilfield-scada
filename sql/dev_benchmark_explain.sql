\timing on

-- Version A: LATERAL. Por cada tag del catalogo, un salto al indice.
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT t.name, m.value, m.ts
FROM tags t
LEFT JOIN LATERAL (
    SELECT value, quality, ts
    FROM measurements
    WHERE tag_id = t.tag_id
    ORDER BY ts DESC
    LIMIT 1
) m ON true
ORDER BY t.equipment, t.variable;

-- Version B: DISTINCT ON. Depende de que Timescale elija su nodo SkipScan.
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT DISTINCT ON (m.tag_id) t.name, m.value, m.ts
FROM measurements m
JOIN tags t ON t.tag_id = m.tag_id
ORDER BY m.tag_id, m.ts DESC;