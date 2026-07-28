using Shared;

var well = new Well("PAD-01/POZO-A", seed: 42);
var stepInterval = TimeSpan.FromSeconds(1);
int elapsed = 0;

Console.WriteLine($"Simulador — {well.Name} — Ctrl+C para salir");
Console.WriteLine("  Freq   Q_oil     I     Vib     THP     CHP  CHP_real  T_head  Status  wear  restr");
Console.WriteLine("  (Hz)   (m3/d)   (A)  (mm/s)   (bar)   (bar)   (bar)     (C)");

while (true)
{
    well.Step(stepInterval.TotalSeconds);

    if (elapsed == 5)
    {
        well.CasingPressure.StartDrift(unitsPerMinute: 6.0);
        Console.WriteLine("  >>> FALLA INYECTADA: deriva en el sensor de CHP <<<");
    }

    if (elapsed == 25)
    {
        well.StartLineObstruction(minutesToFull: 1.5);
        Console.WriteLine("  >>> FALLA INYECTADA: obstrucción de línea <<<");
    }

    Console.WriteLine(
        $"  {well.EspFrequency.Value,5:F2} {well.OilRate.Value,7:F1} {well.EspCurrent.Value,6:F1} " +
        $"{well.EspVibration.Value,6:F2} {well.WellheadPressure.Value,7:F2} " +
        $"{well.CasingPressure.Value,7:F2} {well.CasingPressure.TrueValue,7:F2} " +
        $"{well.HeadTemperature.Value,8:F1}  {well.Status}  " +
        $"{well.PumpWear * 100,4:F0}%  {well.LineRestriction * 100,4:F0}%");

    elapsed++;
    await Task.Delay(stepInterval);
}