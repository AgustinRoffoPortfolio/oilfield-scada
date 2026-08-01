using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Dapper;
using Serilog;
using Shared;
using Alarms;

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
builder.Services.Configure<AlarmOptions>(builder.Configuration.GetSection("Alarms"));

var host = builder.Build();
var db = host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
var opts = host.Services.GetRequiredService<IOptions<AlarmOptions>>().Value;

if (string.IsNullOrWhiteSpace(db.Password))
{
    Log.Error("Falta la variable de entorno Database__Password.");
    return 1;
}

try
{
    await using var conn = new NpgsqlConnection(db.ConnectionString);
    await conn.OpenAsync();

    var repo = new AlarmRepository(db.ConnectionString);
    var limits = await repo.LoadTagLimitsAsync();
    var latest = await repo.ReadLatestAsync();
    var openAlarms = (await repo.LoadOpenAlarmsAsync()).Count;

    var conLimite = limits.Count(l =>
        l.WarnLow is not null || l.WarnHigh is not null ||
        l.AlarmLow is not null || l.AlarmHigh is not null);

    Log.Information("Catalogo: {Total} tags, {ConLimite} con algun umbral. Ultimos valores: {Latest}",
        limits.Count, conLimite, latest.Count);

    Log.Information("Conectado a {Db}@{Host}:{Port}. Alarmas abiertas: {Count}",
        db.Database, db.Host, db.Port, openAlarms);
    Log.Information("Evaluando cada {Interval} ms, histeresis {Hyst}% del rango.",
        opts.PollIntervalMs, opts.HysteresisPercent);
}
catch (Exception ex)
{
    Log.Error(ex, "Fallo el motor de alarmas.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;