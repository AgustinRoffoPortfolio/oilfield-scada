using Serilog;
using Shared;
using Simulator;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

// El mapa del fabricante define la tabla de registros que publica este RTU.
var mapPath = AddressSpaceConfig.Resolve("config/modbusmap.json");
var map = ModbusMap.Load(mapPath);

var field = new Oilfield();
var values = new FieldValues(field);
var table = new RegisterTable(map.HighestAddress + 1, map.WordOrder);
var updater = new RegisterUpdater(map, values, table);

Log.Information("Mapa Modbus: {Path}", mapPath);
Log.Information("Tabla de {Size} registros, orden de palabras {Order}", table.Size, map.WordOrder);

if (updater.MissingTags.Count > 0)
{
    Log.Warning("{Count} registros mapeados sin valor de campo: {Tags}",
        updater.MissingTags.Count, string.Join(", ", updater.MissingTags));
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

const int slavePort = 5502;
var slave = new ModbusTcpSlave(table, map.UnitId, slavePort);
var slaveTask = slave.RunAsync(cts.Token);

// Deja ver los mensajes de arranque antes de tomar la pantalla.
await Task.Delay(1500);
Console.CursorVisible = false;

var stepInterval = TimeSpan.FromSeconds(1);

try
{
    while (!cts.IsCancellationRequested)
    {
        field.Step(stepInterval.TotalSeconds);
        updater.Update();

        HandleKeys(field);
        Draw(field, slave, slavePort, map.UnitId);

        await Task.Delay(stepInterval, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Salida por Ctrl+C.
}

Console.CursorVisible = true;
Console.Clear();
await slaveTask;
Log.Information("RTU detenido.");
await Log.CloseAndFlushAsync();

// ---------- Utilidades ----------

/// Teclas de la demo: inyectar fallas sin recompilar.
static void HandleKeys(Oilfield field)
{
    if (!Console.KeyAvailable) return;

    switch (Console.ReadKey(intercept: true).Key)
    {
        case ConsoleKey.D1: field.GetWell("POZO-B").StartPumpDegradation(minutesToFailure: 2.0); break;
        case ConsoleKey.D2: field.GetWell("POZO-B").RepairPump(); break;
        case ConsoleKey.D3: field.GetWell("POZO-A").StartLineObstruction(minutesToFull: 2.0); break;
        case ConsoleKey.D4: field.GetWell("POZO-A").ClearLine(); break;
    }
}

/// Limpia y redibuja el cuadro entero desde arriba. Mas simple y mas robusto
/// que posicionar el cursor: la consola hace scroll y desacomoda las referencias.
static void Draw(Oilfield field, ModbusTcpSlave slave, int port, byte unitId)
{
    Console.SetCursorPosition(0, 0);
    Console.Clear();

    var masters = slave.ConnectedMasters;
    var link = masters > 0 ? $"{masters} maestro(s) leyendo" : "sin maestros";

    Write($"RTU PAD-01   —   Modbus TCP :{port} unidad {unitId}   —   {link}   —   t = {field.ElapsedSeconds,5:F0} s");
    Write("[1] degradar bomba B   [2] reparar B   [3] obstruir linea A   [4] liberar A   Ctrl+C salir");
    Write("");
    Write("  POZO      Freq    Q_oil  Q_water     Q_gas       I     Vib     THP     CHP  T_head   Estado   wear");
    Write("            (Hz)   (m3/d)   (m3/d)   (Nm3/d)     (A)  (mm/s)   (bar)   (bar)    (C)");

    foreach (var w in field.Wells)
    {
        Write($"  {w.Name,-8} {w.EspFrequency.Value,6:F2} {w.OilRate.Value,8:F1} {w.WaterRate.Value,8:F1} " +
              $"{w.GasRate.Value,9:F0} {w.EspCurrent.Value,7:F1} {w.EspVibration.Value,7:F2} " +
              $"{w.WellheadPressure.Value,7:F2} {w.CasingPressure.Value,7:F2} {w.HeadTemperature.Value,6:F1} " +
              $"  {w.Status,-8} {w.PumpWear * 100,4:F0}%");
    }

    Write($"  {"TOTAL",-8} {"",6} {field.TotalOilRate,8:F1} {field.TotalWaterRate,8:F1} {field.TotalGasRate,9:F0}");
    Write("");
    Write($"  SEPARADOR   P = {field.Separator.Pressure.Value,5:F2} bar     Nivel = {field.Separator.Level.Value,5:F1} %");
    Write($"  DUCTO       P_in = {field.Pipeline.InletPressure.Value,5:F2} bar   " +
          $"P_out = {field.Pipeline.OutletPressure.Value,5:F2} bar   " +
          $"Q_total = {field.Pipeline.TotalFlow.Value,6:F1} m3/d");
}

/// Recorta al ancho de la ventana: si una linea llega justo al borde,
/// la consola agrega un salto extra y descuadra el dibujo.
static void Write(string text)
{
    var width = Math.Max(Console.WindowWidth - 1, 1);
    Console.WriteLine(text.Length > width ? text[..width] : text);
}