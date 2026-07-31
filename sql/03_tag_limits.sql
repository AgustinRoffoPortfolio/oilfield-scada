-- Umbrales operativos por tag. NULL = sin limite definido de ese lado.
-- warn: fuera de la banda normal de operacion. alarm: condicion critica.
-- Son configuracion de planta, no de la pantalla: por eso viven en la base.

ALTER TABLE tags
    ADD COLUMN IF NOT EXISTS warn_low    DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS warn_high   DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS alarm_low   DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS alarm_high  DOUBLE PRECISION;

-- Pozos: la banda normal sale de la tabla de dominio del proyecto.
UPDATE tags SET warn_low = 15,  warn_high = 40,   alarm_low = 10,  alarm_high = 45
    WHERE variable = 'THP';
UPDATE tags SET warn_low = 8,   warn_high = 25,   alarm_low = 5,   alarm_high = 30
    WHERE variable = 'CHP';
UPDATE tags SET warn_low = 40,  warn_high = 95,   alarm_low = 30,  alarm_high = 105
    WHERE variable = 'T_head';
-- 120 es el rango nominal, pero POZO-C opera cerca de 146: el limite va con margen
-- sobre lo que el proceso hace de verdad, o la operacion normal genera alarmas.
UPDATE tags SET warn_low = 5,   warn_high = 160,  alarm_low = 2,  alarm_high = 190
    WHERE variable = 'Q_oil';
UPDATE tags SET warn_high = 200, alarm_high = 240
    WHERE variable = 'Q_water';
UPDATE tags SET warn_low = 500, warn_high = 20000
    WHERE variable = 'Q_gas';
UPDATE tags SET warn_low = 20,  warn_high = 75,   alarm_high = 85
    WHERE variable = 'ESP_current';
UPDATE tags SET warn_low = 35,  warn_high = 60
    WHERE variable = 'ESP_freq';
-- La vibracion no tiene limite bajo: cuanto menos, mejor.
UPDATE tags SET warn_high = 5.5, alarm_high = 7.0
    WHERE variable = 'ESP_vib';

-- Separador y ducto.
UPDATE tags SET warn_low = 6,   warn_high = 14,   alarm_low = 4,   alarm_high = 18
    WHERE variable = 'Sep_P';
UPDATE tags SET warn_low = 25,  warn_high = 75,   alarm_low = 15,  alarm_high = 85
    WHERE variable = 'Sep_level';
UPDATE tags SET warn_low = 6,   warn_high = 14,   alarm_high = 18
    WHERE variable = 'Pipe_P_in';
UPDATE tags SET warn_low = 2,   warn_high = 8
    WHERE variable = 'Pipe_P_out';
UPDATE tags SET warn_low = 50,  warn_high = 600
    WHERE variable = 'Pipe_Q';