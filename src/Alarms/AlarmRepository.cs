using Dapper;
using Npgsql;

namespace Alarms;

/// Acceso a la base para el motor de alarmas.
public sealed class AlarmRepository(string connectionString)
{
    private readonly string _cs = connectionString;

    /// Catalogo completo con umbrales. Se lee al arrancar: es configuracion de planta,
    /// no cambia en cada ciclo.
    public async Task<IReadOnlyList<TagLimits>> LoadTagLimitsAsync()
    {
        await using var conn = new NpgsqlConnection(_cs);
        var rows = await conn.QueryAsync<TagLimits>("""
            SELECT tag_id      AS TagId,
                   name        AS Name,
                   equipment   AS Equipment,
                   variable    AS Variable,
                   unit        AS Unit,
                   eu_min      AS EuMin,
                   eu_max      AS EuMax,
                   warn_low    AS WarnLow,
                   warn_high   AS WarnHigh,
                   alarm_low   AS AlarmLow,
                   alarm_high  AS AlarmHigh
            FROM tags
            ORDER BY tag_id
            """);
        return rows.ToList();
    }

    /// Ultimo valor de cada tag. Misma consulta que usa la WebApp.
    public async Task<IReadOnlyList<LatestValue>> ReadLatestAsync()
    {
        await using var conn = new NpgsqlConnection(_cs);
        var rows = await conn.QueryAsync<LatestValue>("""
            SELECT DISTINCT ON (tag_id)
                   tag_id  AS TagId,
                   ts      AS Ts,
                   value   AS Value,
                   quality AS Quality
            FROM measurements
            ORDER BY tag_id, ts DESC
            """);
        return rows.ToList();
    }

    /// Alarmas que siguen abiertas. Es el estado que el motor tiene que continuar,
    /// no reinventar: si se reinicia, retoma lo que quedo en la base.
    public async Task<IReadOnlyList<OpenAlarm>> LoadOpenAlarmsAsync()
    {
        await using var conn = new NpgsqlConnection(_cs);
        var rows = await conn.QueryAsync<OpenAlarm>("""
            SELECT alarm_id    AS AlarmId,
                   tag_id      AS TagId,
                   severity    AS Severity,
                   direction   AS Direction,
                   limit_value AS LimitValue,
                   raised_at   AS RaisedAt,
                   acked_at    AS AckedAt
            FROM alarm_events
            WHERE cleared_at IS NULL
            """);
        return rows.ToList();
    }
    
    /// Inserta una alarma nueva y devuelve su id.
    /// ON CONFLICT DO NOTHING protege contra la carrera de dos evaluaciones
    /// simultaneas: el indice unico parcial ya garantiza una sola abierta por
    /// tag+severidad+lado, y aca preferimos ignorar el duplicado antes que morir.
    public async Task<long?> RaiseAsync(RaiseAction a)
    {
        await using var conn = new NpgsqlConnection(_cs);
        return await conn.ExecuteScalarAsync<long?>("""
            INSERT INTO alarm_events
                (tag_id, severity, direction, limit_value, raise_value, raised_at)
            VALUES (@TagId, @Severity, @Direction, @LimitValue, @RaiseValue, @RaisedAt)
            ON CONFLICT DO NOTHING
            RETURNING alarm_id
            """,
            new
            {
                TagId = a.Tag.TagId,
                a.Severity,
                a.Direction,
                a.LimitValue,
                RaiseValue = a.Value,
                RaisedAt = a.Ts
            });
    }

    /// Marca una alarma como normalizada. El WHERE evita pisar una ya cerrada.
    public async Task<bool> ClearAsync(ClearAction c)
    {
        await using var conn = new NpgsqlConnection(_cs);
        var rows = await conn.ExecuteAsync("""
            UPDATE alarm_events
            SET cleared_at = @Ts, clear_value = @Value
            WHERE alarm_id = @AlarmId AND cleared_at IS NULL
            """,
            new { AlarmId = c.Alarm.AlarmId, c.Ts, c.Value });
        return rows > 0;
    }
}