using Dapper;
using Npgsql;

namespace WebApp;

/// Consultas de lectura sobre TimescaleDB.
public sealed class ReadingRepository(NpgsqlDataSource dataSource)
{
    /// Puntos que apuntamos a devolver, sin importar la ventana pedida.
    /// Por encima del ancho en pixeles del grafico no se gana nada.
    private const int TargetPoints = 600;

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

    // time_bucket parte el tiempo en intervalos regulares y resume cada uno.
    // Es la funcion propia de TimescaleDB para series temporales.
    private const string HistorySql = """
        SELECT time_bucket(@bucket::interval, m.ts) AS ts,
               avg(m.value) AS avg,
               min(m.value) AS min,
               max(m.value) AS max
        FROM measurements m
        JOIN tags t ON t.tag_id = m.tag_id
        WHERE t.name = @name
          AND m.ts >= now() - @span::interval
        GROUP BY 1
        ORDER BY 1
        """;

    public async Task<IReadOnlyList<TagReading>> GetLatestAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<TagReading>(new CommandDefinition(LatestSql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(
        string name, int minutes, CancellationToken ct)
    {
        var span = TimeSpan.FromMinutes(minutes);
        // Un bucket mas chico que el intervalo de publicacion no aporta nada.
        var bucket = TimeSpan.FromSeconds(Math.Max(1, span.TotalSeconds / TargetPoints));

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<HistoryPoint>(new CommandDefinition(
            HistorySql, new { name, bucket, span }, cancellationToken: ct));
        return rows.AsList();
    }
}