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
    var engine = new AlarmEngine(opts);

    var limits = await repo.LoadTagLimitsAsync();
    var openAlarms = (await repo.LoadOpenAlarmsAsync()).Count;

    Log.Information("Conectado a {Db}@{Host}:{Port}. Catalogo: {Tags} tags, {Open} alarmas abiertas.",
        db.Database, db.Host, db.Port, limits.Count, openAlarms);
    Log.Information("Evaluando cada {Interval} ms, histeresis {Hyst}% del rango.",
        opts.PollIntervalMs, opts.HysteresisPercent);

    using var cts = new CancellationTokenSource();
    var loop = EvaluationLoopAsync(repo, engine, opts, cts.Token);

    Log.Information("Motor de alarmas corriendo. Enter para detener.");
    Console.ReadLine();

    await cts.CancelAsync();
    await loop;
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

/// Cada intervalo: lee ultimos valores y alarmas abiertas, evalua, y aplica los cambios.
/// El estado vive en la base, no en memoria: si el proceso se reinicia, retoma solo.
static async Task EvaluationLoopAsync(
    AlarmRepository repo, AlarmEngine engine,
    AlarmOptions opts, CancellationToken ct)
{
    var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(opts.PollIntervalMs));

    while (true)
    {
        try { await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { break; }

        try
        {
            // El catalogo se relee en cada ciclo: son 35 filas y asi un cambio de
            // umbral en la base tiene efecto sin reiniciar el motor.
            var limits = await repo.LoadTagLimitsAsync();
            var byId = limits.ToDictionary(t => t.TagId);
            var latest = (await repo.ReadLatestAsync()).ToDictionary(v => v.TagId);
            var open = (await repo.LoadOpenAlarmsAsync()).ToDictionary(a => a.TagId);

            var result = engine.Evaluate(limits, latest, open, DateTime.UtcNow);

            // Primero los cierres: si un tag escala de warn a alarm, hay que liberar
            // el indice unico antes de insertar la nueva.
            foreach (var c in result.ToClear)
            {
                if (await repo.ClearAsync(c))
                    Log.Information("NORMALIZADA {Tag} {Sev} {Dir} en {Value:F2}",
                        byId[c.Alarm.TagId].Name, c.Alarm.Severity, c.Alarm.Direction, c.Value);
            }

            foreach (var r in result.ToRaise)
            {
                var id = await repo.RaiseAsync(r);
                if (id is not null)
                    Log.Warning("ALARMA #{Id} {Tag} {Sev} {Dir}: {Value:F2} cruzo {Limit:F2} {Unit}",
                        id, r.Tag.Name, r.Severity, r.Direction, r.Value, r.LimitValue, r.Tag.Unit);
            }
        }
        catch (Exception ex)
        {
            // Un ciclo que falla no puede matar el motor: se loguea y se sigue.
            Log.Error(ex, "Fallo un ciclo de evaluacion.");
        }
    }

    Log.Information("Motor de alarmas detenido.");
}