using Npgsql;
using Serilog;
using Shared;
using WebApp;

var builder = WebApplication.CreateBuilder(args);

// Serilog reemplaza el logger por defecto de .NET.
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .WriteTo.Console());

// Lee la seccion "Database" del appsettings y la mapea a la clase.
var dbOptions = builder.Configuration.GetSection("Database").Get<DatabaseOptions>()
    ?? throw new InvalidOperationException("Falta la seccion Database en appsettings.json");

// Una sola fabrica de conexiones para toda la app, con pool interno.
var dataSource = new NpgsqlDataSourceBuilder(dbOptions.ConnectionString).Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ReadingRepository>();

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

app.Run();