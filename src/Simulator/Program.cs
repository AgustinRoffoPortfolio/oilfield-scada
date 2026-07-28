using Shared;

var field = new Oilfield();
var stepInterval = TimeSpan.FromSeconds(1);
int elapsed = 0;

Console.Clear();
Console.CursorVisible = false;

while (true)
{
    field.Step(stepInterval.TotalSeconds);

    if (elapsed == 15)
        field.GetWell("POZO-B").StartPumpDegradation(minutesToFailure: 2.0);

    Console.SetCursorPosition(0, 0);
    Write($"YACIMIENTO PAD-01   —   t = {field.ElapsedSeconds,4:F0} s   —   Ctrl+C para salir");
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

    elapsed++;
    await Task.Delay(stepInterval);
}

// Escribe una línea rellenando con espacios, para que no queden restos del cuadro anterior.
static void Write(string text) => Console.WriteLine(text.PadRight(Console.WindowWidth - 1));