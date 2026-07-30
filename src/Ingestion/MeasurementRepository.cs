using Npgsql;
using NpgsqlTypes;

namespace Ingestion;

/// Escritura masiva en la hypertable de mediciones.
public sealed class MeasurementRepository(string connectionString)
{
    /// Escribe el lote con COPY binario: una sola operacion para todas las filas.
    public async Task<int> WriteBatchAsync(
        IReadOnlyList<Measurement> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;

        // El indice unico (tag_id, ts) rechaza duplicados y COPY aborta el lote entero.
        // Si el servidor repitio un instante, nos quedamos con la ultima lectura.
        var rows = batch
            .GroupBy(m => (m.TagId, m.Timestamp))
            .Select(g => g.Last())
            .ToList();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY measurements (ts, tag_id, value, quality) FROM STDIN (FORMAT BINARY)", ct);

        foreach (var m in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(m.Timestamp, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(m.TagId, NpgsqlDbType.Smallint, ct);
            await writer.WriteAsync(m.Value, NpgsqlDbType.Double, ct);
            await writer.WriteAsync(m.Quality, NpgsqlDbType.Smallint, ct);
        }

        await writer.CompleteAsync(ct);
        return rows.Count;
    }
}