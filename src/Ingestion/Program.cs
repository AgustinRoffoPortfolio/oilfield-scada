using Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Dapper;
using Serilog;
using Shared;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));

var host = builder.Build();
var db = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

if (string.IsNullOrWhiteSpace(db.Password))
{
    Log.Error("Falta la variable de entorno Database__Password.");
    return 1;
}

try
{
    await using var conn = new NpgsqlConnection(db.ConnectionString);
    await conn.OpenAsync();

    var version = await conn.QuerySingleAsync<string>(
        "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb'");

    var fieldTags = FieldTagCatalog.Build(new Oilfield());
    var tagRepo = new TagRepository(db.ConnectionString);
    var tagIds = await tagRepo.SyncAsync(fieldTags);
    Log.Information("Catalogo sincronizado: {Count} tags en la base", tagIds.Count);

    Log.Information("Conectado a {Db}@{Host}:{Port} - TimescaleDB {Version}",
        db.Database, db.Host, db.Port, version);
}
catch (Exception ex)
{
    Log.Error(ex, "No se pudo conectar a la base.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;