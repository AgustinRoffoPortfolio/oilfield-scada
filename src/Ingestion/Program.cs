using System.Collections.Concurrent;
using System.Globalization;
using Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Dapper;
using Opc.Ua;
using Serilog;
using Shared;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddSerilog();
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<OpcUaOptions>(builder.Configuration.GetSection("OpcUa"));


var host = builder.Build();
var db = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
var opc = host.Services.GetRequiredService<IOptions<OpcUaOptions>>().Value;

if (string.IsNullOrWhiteSpace(db.Password))
{
    Log.Error("Falta la variable de entorno Database__Password.");
    return 1;
}

// Cola compartida: la escribe el hilo de OPC UA, la vacia el loop de volcado.
var buffer = new ConcurrentQueue<Measurement>();
using var cts = new CancellationTokenSource();

try
{
    await using var conn = new NpgsqlConnection(db.ConnectionString);
    await conn.OpenAsync();

    var version = await conn.QuerySingleAsync<string>(
        "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb'");

    Log.Information("Conectado a {Db}@{Host}:{Port} - TimescaleDB {Version}",
        db.Database, db.Host, db.Port, version);

    var addressSpacePath = AddressSpaceConfig.Resolve(
            builder.Configuration["AddressSpaceFile"] ?? "config/addressspace.json");
    var addressSpace = AddressSpaceConfig.Load(addressSpacePath);
    var fieldTags = FieldTagCatalog.Build(addressSpace);
    Log.Information("Address space: {Path}", addressSpacePath);
    var tagRepo = new TagRepository(db.ConnectionString);
    var tagIds = await tagRepo.SyncAsync(fieldTags);
    Log.Information("Catalogo sincronizado: {Count} tags en la base", tagIds.Count);

    var measurements = new MeasurementRepository(db.ConnectionString);

    var opcClient = new OpcUaClient(opc);
    await opcClient.ConnectAsync();

    await opcClient.SubscribeAsync(fieldTags, (name, dv) =>
    {
        if (!tagIds.TryGetValue(name, out var tagId) || dv.Value is null) return;

        double value;
        try { value = Convert.ToDouble(dv.Value, CultureInfo.InvariantCulture); }
        catch { return; }

        var ts = dv.SourceTimestamp != DateTime.MinValue ? dv.SourceTimestamp : DateTime.UtcNow;
        buffer.Enqueue(new Measurement(ts, tagId, value, QualityOf(dv.StatusCode)));
    });

    var flushTask = FlushLoopAsync(measurements, buffer, db.FlushIntervalMs, cts.Token);

    Log.Information("Ingesta corriendo. Enter para detener.");
    Console.ReadLine();

    await cts.CancelAsync();
    await flushTask;
    await opcClient.DisconnectAsync();
}
catch (Exception ex)
{
    Log.Error(ex, "Fallo la ingesta.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

/// Traduce el StatusCode de OPC UA a nuestra calidad resumida.
static short QualityOf(StatusCode status) =>
    StatusCode.IsGood(status) ? (short)0 :
    StatusCode.IsUncertain(status) ? (short)1 : (short)2;

/// Vacia el buffer a la base cada intervalo, hasta que se cancele.
static async Task FlushLoopAsync(
    MeasurementRepository repo, ConcurrentQueue<Measurement> buffer,
    int intervalMs, CancellationToken ct)
{
    var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
    long total = 0;

    while (true)
    {
        try { await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { break; }

        total += await DrainAsync();
    }

    // Ultimo volcado antes de salir, para no perder lo que quedo encolado.
    total += await DrainAsync();
    Log.Information("Ingesta detenida. Total escrito: {Total} filas", total);

    async Task<int> DrainAsync()
    {
        var batch = new List<Measurement>();
        while (buffer.TryDequeue(out var m)) batch.Add(m);
        if (batch.Count == 0) return 0;

        try
        {
            var written = await repo.WriteBatchAsync(batch, CancellationToken.None);
            Log.Information("Volcadas {Count} filas", written);
            return written;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo el volcado de {Count} filas", batch.Count);
            return 0;
        }
    }
}