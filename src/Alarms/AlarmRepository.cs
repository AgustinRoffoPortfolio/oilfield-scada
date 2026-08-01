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
}