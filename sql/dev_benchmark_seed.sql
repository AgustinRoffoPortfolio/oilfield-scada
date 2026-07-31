-- Datos sinteticos SOLO para medir planes de consulta. No forman parte del sistema.
-- Se insertan en una ventana de hace 90 dias, para no chocar con el indice unico
-- (tag_id, ts) de los datos reales ni cambiar cual es el "ultimo valor".
INSERT INTO measurements (ts, tag_id, value, quality)
SELECT g.ts,
       t.tag_id,
       COALESCE(t.eu_min, 0)
         + (COALESCE(t.eu_max, 100) - COALESCE(t.eu_min, 0))
         * (0.5 + 0.4 * sin(EXTRACT(EPOCH FROM g.ts) / 600.0 + t.tag_id)),
       0
FROM tags t
CROSS JOIN generate_series(
        now() - interval '90 days',
        now() - interval '86 days',
        interval '2 seconds') AS g(ts);