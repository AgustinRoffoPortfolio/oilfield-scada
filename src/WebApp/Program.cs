using Npgsql;
using Serilog;
using Shared;
using WebApp;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console());

var dbOptions = builder.Configuration.GetSection("Database").Get<DatabaseOptions>()
    ?? throw new InvalidOperationException("Falta la seccion Database en appsettings.json");

var dataSource = new NpgsqlDataSourceBuilder(dbOptions.ConnectionString).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ReadingRepository>();

// Una sola instancia con dos roles: tarea de fondo y objeto inyectable en el endpoint.
builder.Services.AddSingleton<LatestBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LatestBroadcaster>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Prueba de vida: pregunta la hora a Postgres.
app.MapGet("/api/health", async (NpgsqlDataSource source) =>
{
    await using var conn = await source.OpenConnectionAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT now()";
    var ts = await cmd.ExecuteScalarAsync();
    return Results.Ok(new { database = "ok", serverTime = ts });
});

// Ultimo valor de los 35 tags, para pintar el dashboard al cargar la pagina.
app.MapGet("/api/tags/latest", async (ReadingRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetLatestAsync(ct)));

// Historial de un tag, agregado en la base para devolver siempre ~600 puntos.
// El nombre va por query string porque contiene "/" (POZO-A/THP).
app.MapGet("/api/history", async (string tag, int? minutes,
                                  ReadingRepository repo, CancellationToken ct) =>
{
    var window = Math.Clamp(minutes ?? 30, 1, 1440);
    var points = await repo.GetHistoryAsync(tag, window, ct);
    return Results.Ok(points);
});

// Stream de actualizaciones. La respuesta nunca termina: escucha al broadcaster
// y escribe un bloque "data: {...}" cada vez que llega un snapshot nuevo.
app.MapGet("/api/stream", async (HttpContext http, LatestBroadcaster broadcaster,
                                 ILogger<Program> log, CancellationToken ct) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    var channel = broadcaster.Subscribe();
    log.LogInformation("SSE conectado desde {Ip}", http.Connection.RemoteIpAddress);
    try
    {
        // Snapshot inmediato para que la pagina no arranque vacia.
        if (broadcaster.Last is string last)
        {
            await http.Response.WriteAsync($"data: {last}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }

        await foreach (var json in channel.Reader.ReadAllAsync(ct))
        {
            await http.Response.WriteAsync($"data: {json}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // El navegador cerro la pestaña. Es el final normal, no un error.
    }
    finally
    {
        broadcaster.Unsubscribe(channel);
        log.LogInformation("SSE desconectado");
    }
});

app.Run();