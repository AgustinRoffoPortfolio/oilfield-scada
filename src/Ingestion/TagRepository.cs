using Dapper;
using Npgsql;
using Shared;

namespace Ingestion;

/// Acceso a la tabla de catalogo de tags.
public sealed class TagRepository(string connectionString)
{
    /// Inserta los tags que falten y devuelve el mapa nombre -> tag_id.
    /// Es idempotente: correrlo mil veces deja la tabla igual.
    public async Task<Dictionary<string, short>> SyncAsync(IReadOnlyList<FieldTag> tags)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string insert = """
            INSERT INTO tags (name, equipment, variable, unit, eu_min, eu_max)
            VALUES (@Name, @Equipment, @Variable, @Unit, @Low, @High)
            ON CONFLICT (name) DO NOTHING
            """;

        var inserted = await conn.ExecuteAsync(insert, tags);

        var rows = await conn.QueryAsync<(string Name, short TagId)>(
            "SELECT name, tag_id FROM tags");

        var map = rows.ToDictionary(r => r.Name, r => r.TagId);
        return map;
    }
}