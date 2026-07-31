using Dapper;
using Npgsql;

namespace WebApp;

/// Consultas de lectura sobre TimescaleDB.
public sealed class ReadingRepository(NpgsqlDataSource dataSource)
{
    // LATERAL: por cada tag del catalogo, un salto al indice por su ultima medicion.
    // LEFT JOIN para que un tag sin datos aparezca igual, con value en null.
    private const string LatestSql = """
        SELECT t.name, t.equipment, t.variable, t.unit,
               t.eu_min AS EuMin, t.eu_max AS EuMax,
               m.value, m.quality, m.ts
        FROM tags t
        LEFT JOIN LATERAL (
            SELECT value, quality, ts
            FROM measurements
            WHERE tag_id = t.tag_id
            ORDER BY ts DESC
            LIMIT 1
        ) m ON true
        ORDER BY t.equipment, t.variable
        """;

    public async Task<IReadOnlyList<TagReading>> GetLatestAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TagReading>(new CommandDefinition(LatestSql, cancellationToken: ct));
        return rows.AsList();
    }
}