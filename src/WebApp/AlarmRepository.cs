using Dapper;
using Npgsql;

namespace WebApp;

/// Consultas y comandos del panel de alarmas.
public sealed class AlarmRepository(NpgsqlDataSource dataSource)
{
    // El estado no es una columna: se deriva de las marcas de tiempo, para que no
    // pueda quedar desincronizado de ellas.
    private const string StateExpr = """
        CASE
            WHEN a.cleared_at IS NULL AND a.acked_at IS NULL THEN 'active'
            WHEN a.cleared_at IS NULL                        THEN 'acked'
            WHEN a.acked_at IS NULL                          THEN 'unacked_cleared'
            ELSE 'closed'
        END
        """;

    private const string SelectSql = $"""
        SELECT a.alarm_id AS AlarmId, t.name AS Name, t.equipment AS Equipment,
               t.variable AS Variable, t.unit AS Unit,
               a.severity AS Severity, a.direction AS Direction,
               a.limit_value AS LimitValue, a.raise_value AS RaiseValue,
               a.raised_at AS RaisedAt, a.acked_at AS AckedAt,
               a.cleared_at AS ClearedAt, a.clear_value AS ClearValue,
               {StateExpr} AS State
        FROM alarm_events a
        JOIN tags t ON t.tag_id = a.tag_id
        """;

    /// Lo que el operador tiene pendiente: sin normalizar, o normalizadas sin reconocer.
    /// Una alarma que volvio a normal pero nadie vio sigue en pantalla a proposito.
    public async Task<IReadOnlyList<AlarmView>> GetPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AlarmView>(new CommandDefinition($"""
            {SelectSql}
            WHERE a.cleared_at IS NULL OR a.acked_at IS NULL
            ORDER BY a.raised_at DESC
            """, cancellationToken: ct));
        return rows.AsList();
    }

    /// Historial completo, para el registro de eventos.
    public async Task<IReadOnlyList<AlarmView>> GetHistoryAsync(int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AlarmView>(new CommandDefinition($"""
            {SelectSql}
            ORDER BY a.raised_at DESC
            LIMIT @limit
            """, new { limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// Reconocer: el operador avisa que la vio. No cambia el valor del proceso,
    /// solo deja constancia de que alguien se hizo cargo.
    /// Devuelve false si la alarma no existe o ya estaba reconocida.
    public async Task<bool> AcknowledgeAsync(long alarmId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE alarm_events
            SET acked_at = now()
            WHERE alarm_id = @alarmId AND acked_at IS NULL
            """, new { alarmId }, cancellationToken: ct));
        return rows > 0;
    }
}